using System.Net;
using System.Net.Http.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Infrastructure.Storage;

/// <summary>
/// Development/E2E stand-in for R2: a directory with the same key layout. Served by the API's
/// /cdn endpoint with the same immutable cache headers the Worker uses.
/// </summary>
public sealed class LocalObjectStore(StoragePaths paths) : IObjectStore
{
    public string Name => "local";

    public async Task PutFileAsync(string key, string filePath, string contentType, string? sha256, CancellationToken ct)
    {
        var target = paths.LocalObjectPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var src = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var dst = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous))
            {
                await src.CopyToAsync(dst, ct);
            }

            File.Move(temp, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new ObjectStoreException($"Local store write failed for {key}.", ex);
        }
    }

    public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct)
    {
        var file = new FileInfo(paths.LocalObjectPath(key));
        return Task.FromResult(file.Exists ? new StoredObjectInfo(file.Length, null) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        TryDelete(paths.LocalObjectPath(key));
        return Task.CompletedTask;
    }

    public string? ResolveFile(string key)
    {
        var path = paths.LocalObjectPath(key);
        return File.Exists(path) ? path : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// Cloudflare R2 through its S3-compatible API. Newer AWS SDKs send CRC32 checksums by default,
/// which R2 rejects, so checksum calculation/validation is limited to WHEN_REQUIRED.
/// </summary>
public sealed class R2ObjectStore : IObjectStore, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly ILogger<R2ObjectStore> _logger;
    private readonly bool _https;

    public R2ObjectStore(R2Settings settings, ILogger<R2ObjectStore> logger)
    {
        _bucket = settings.Bucket;
        _logger = logger;
        _https = settings.ResolvedEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var config = new AmazonS3Config
        {
            ServiceURL = settings.ResolvedEndpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromMinutes(2),
            MaxErrorRetry = 3,
        };
        _client = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config);
    }

    public string Name => "r2";

    public async Task PutFileAsync(string key, string filePath, string contentType, string? sha256, CancellationToken ct)
    {
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                FilePath = filePath,
                ContentType = contentType,
                // Streaming without payload hashing is only allowed over TLS (always the case for R2).
                DisablePayloadSigning = _https,
            };
            request.Headers.CacheControl = "public, max-age=31536000, immutable";
            if (!string.IsNullOrEmpty(sha256))
            {
                request.Metadata["sha256"] = sha256;
            }

            await _client.PutObjectAsync(request, ct);
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogError("R2 put failed for {Key}: {Status} {Code}", key, ex.StatusCode, ex.ErrorCode);
            throw new ObjectStoreException($"R2 put failed for {key}.", ex);
        }
        catch (Exception ex) when (ex is AmazonClientException or HttpRequestException or System.Net.Sockets.SocketException or TimeoutException)
        {
            throw new ObjectStoreException($"R2 put failed for {key}.", ex);
        }
    }

    public async Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct)
    {
        try
        {
            var meta = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = key }, ct);
            return new StoredObjectInfo(meta.ContentLength, meta.Metadata["sha256"]);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonServiceException ex)
        {
            throw new ObjectStoreException($"R2 head failed for {key}.", ex);
        }
        catch (Exception ex) when (ex is AmazonClientException or HttpRequestException or System.Net.Sockets.SocketException or TimeoutException)
        {
            throw new ObjectStoreException($"R2 head failed for {key}.", ex);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        try
        {
            await _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = key }, ct);
        }
        catch (AmazonServiceException ex)
        {
            throw new ObjectStoreException($"R2 delete failed for {key}.", ex);
        }
        catch (Exception ex) when (ex is AmazonClientException or HttpRequestException or System.Net.Sockets.SocketException or TimeoutException)
        {
            throw new ObjectStoreException($"R2 delete failed for {key}.", ex);
        }
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Purges URLs from the Cloudflare edge cache (only when a zone id + API token are configured).</summary>
public sealed class CloudflareCdnPurger(IHttpClientFactory httpClientFactory, CloudflareSettings settings, ILogger<CloudflareCdnPurger> logger) : ICdnPurger
{
    public bool IsEnabled => settings.PurgeEnabled;

    public async Task PurgeAsync(IReadOnlyCollection<string> urls, CancellationToken ct)
    {
        if (!IsEnabled || urls.Count == 0)
        {
            return;
        }

        var client = httpClientFactory.CreateClient("cloudflare");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/zones/{settings.ZoneId}/purge_cache")
        {
            Content = JsonContent.Create(new { files = urls }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiToken);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Cloudflare purge failed with {Status}", (int)response.StatusCode);
            throw new ObjectStoreException($"Cloudflare purge failed ({(int)response.StatusCode}).");
        }
    }
}
