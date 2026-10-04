using Amazon.Runtime;
using Amazon.S3;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComicPlatform.IntegrationTests;

/// <summary>Runs only when an S3-compatible endpoint is provided (e.g. MinIO in Docker), otherwise skipped.</summary>
public sealed class S3FactAttribute : FactAttribute
{
    public S3FactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("S3_TEST_ENDPOINT")))
        {
            Skip = "Set S3_TEST_ENDPOINT/S3_TEST_ACCESS_KEY/S3_TEST_SECRET_KEY (e.g. MinIO) to run the R2 client contract test.";
        }
    }
}

/// <summary>Contract test of the production R2 client (AWS SDK, R2-compatible checksum settings) against MinIO.</summary>
public class R2ObjectStoreTests
{
    [S3Fact]
    public async Task Put_head_delete_round_trip()
    {
        var settings = new R2Settings
        {
            Endpoint = Environment.GetEnvironmentVariable("S3_TEST_ENDPOINT")!,
            AccessKey = Environment.GetEnvironmentVariable("S3_TEST_ACCESS_KEY") ?? "minioadmin",
            SecretKey = Environment.GetEnvironmentVariable("S3_TEST_SECRET_KEY") ?? "minioadmin",
            Bucket = "comicplatform-test-" + Guid.NewGuid().ToString("N")[..8],
        };

        using (var admin = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
                   new AmazonS3Config { ServiceURL = settings.Endpoint, ForcePathStyle = true, AuthenticationRegion = "auto" }))
        {
            await admin.PutBucketAsync(settings.Bucket);
        }

        using var store = new R2ObjectStore(settings, NullLogger<R2ObjectStore>.Instance);
        var key = ObjectKeys.Package(Guid.NewGuid(), Guid.NewGuid(), 1);
        var file = Path.GetTempFileName();
        var data = new byte[3 * 1024 * 1024 + 17];
        Random.Shared.NextBytes(data);
        await File.WriteAllBytesAsync(file, data);
        try
        {
            Assert.Null(await store.HeadAsync(key, CancellationToken.None));
            await store.PutFileAsync(key, file, "application/octet-stream", "abc123", CancellationToken.None);
            var head = await store.HeadAsync(key, CancellationToken.None);
            Assert.NotNull(head);
            Assert.Equal(data.Length, head!.Size);
            Assert.Equal("abc123", head.Sha256);
            await store.DeleteAsync(key, CancellationToken.None);
            Assert.Null(await store.HeadAsync(key, CancellationToken.None));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [S3Fact]
    public async Task Bad_credentials_surface_as_object_store_exceptions()
    {
        var settings = new R2Settings { Endpoint = Environment.GetEnvironmentVariable("S3_TEST_ENDPOINT")!, AccessKey = "wrong", SecretKey = "wrong-secret", Bucket = "nope" };
        using var store = new R2ObjectStore(settings, NullLogger<R2ObjectStore>.Instance);
        await Assert.ThrowsAsync<ObjectStoreException>(() => store.HeadAsync("comics/x", CancellationToken.None));
    }
}
