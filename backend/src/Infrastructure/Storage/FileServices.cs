using System.Security.Cryptography;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Common;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Imaging;
using ComicPlatform.Infrastructure.Processing;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Infrastructure.Storage;

/// <summary>Streams uploads to the temp area under a hard byte cap (the body is never buffered in memory).</summary>
public sealed class UploadStore(StoragePaths paths, ILogger<UploadStore> logger) : IUploadStore
{
    private static readonly byte[] ZipLocalHeader = [0x50, 0x4B, 0x03, 0x04];

    public async Task<SavedUpload> SaveAsync(Guid uploadId, Stream content, long maxBytes, CancellationToken ct)
    {
        var final = paths.UploadPath(uploadId);
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        var partial = final + ".part";
        long total = 0;
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81_920];
                int read;
                var first = true;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw Errors.PayloadTooLarge($"Tệp tải lên vượt quá giới hạn {maxBytes / (1024 * 1024)} MB.");
                    }

                    if (first)
                    {
                        // Enough bytes for the signature are virtually always in the first read.
                        if (read < 4 || !buffer.AsSpan(0, 4).SequenceEqual(ZipLocalHeader))
                        {
                            throw Errors.BadRequest("invalid_archive", "Tệp tải lên phải là tệp nén ZIP hoặc CBZ.");
                        }

                        first = false;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                if (first)
                {
                    throw Errors.BadRequest("empty_upload", "Tệp tải lên rỗng.");
                }
            }

            File.Move(partial, final);
            return new SavedUpload(final, total);
        }
        catch
        {
            Delete(partial);
            throw;
        }
    }

    public bool Exists(string path) => IsUnderTemp(path) && File.Exists(path);

    public void Delete(string path)
    {
        if (!IsUnderTemp(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not delete temp upload");
        }
    }

    private bool IsUnderTemp(string path) =>
        Path.GetFullPath(path).StartsWith(paths.TempRoot, StringComparison.OrdinalIgnoreCase);
}

public sealed class ChapterFiles(StoragePaths paths, ILogger<ChapterFiles> logger) : IChapterFiles
{
    public void DeleteChapter(Guid comicId, Guid chapterId) => DeleteDirectory(paths.ChapterDir(comicId, chapterId));

    public void DeleteVersion(Guid comicId, Guid chapterId, int version) => DeleteDirectory(paths.ChapterVersionDir(comicId, chapterId, version));

    public void DeleteComic(Guid comicId) => DeleteDirectory(paths.ComicDir(comicId));

    public bool PackageExists(Guid comicId, Guid chapterId, int version) => File.Exists(paths.PackagePath(comicId, chapterId, version));

    private void DeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete directory {Directory}", dir);
        }
    }
}

/// <summary>Cover and logo images: validate → WebP thumbnail (bounded worker pool) → block volume → object store.</summary>
public sealed class MediaService(
    StoragePaths paths,
    IObjectStore store,
    ImageWorkerPool pool,
    NetVipsImageProcessor processor,
    LimitSettings limits) : IMediaService
{
    public Task<StoredImage> StoreCoverAsync(Guid comicId, Stream input, CancellationToken ct) =>
        StoreAsync(input, 600, 900, hash => ObjectKeys.Cover(comicId, hash), ct);

    public Task<StoredImage> StoreLogoAsync(Guid houseId, Stream input, CancellationToken ct) =>
        StoreAsync(input, 256, 256, hash => ObjectKeys.Logo(houseId, hash), ct);

    private async Task<StoredImage> StoreAsync(Stream input, int maxWidth, int maxHeight, Func<string, string> keyFor, CancellationToken ct)
    {
        var work = Path.Combine(paths.TempRoot, "work", "media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var raw = Path.Combine(work, "input.bin");
            long total = 0;
            await using (var output = new FileStream(raw, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81_920];
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > limits.MaxCoverSize)
                    {
                        throw Errors.PayloadTooLarge($"Ảnh vượt quá giới hạn {limits.MaxCoverSize / (1024 * 1024)} MB.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            var (format, animated) = await ImageSignature.DetectFileAsync(raw, ct);
            if (format == ImageFormat.Unknown || animated)
            {
                throw Errors.BadRequest("unsupported_image", "Chỉ hỗ trợ ảnh JPEG, PNG hoặc WebP (không động).");
            }

            var outPath = Path.Combine(work, "output.webp");
            PageResult result;
            try
            {
                result = await pool.RunAsync(() => processor.CreateThumbnail(raw, outPath, maxWidth, maxHeight), ct);
            }
            catch (PipelineException ex)
            {
                throw Errors.BadRequest(ex.Code, ex.Message);
            }

            string hash;
            await using (var fs = File.OpenRead(outPath))
            {
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct))[..16];
            }

            var key = keyFor(hash);
            var assetPath = paths.AssetPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            File.Move(outPath, assetPath, overwrite: true);
            await store.PutFileAsync(key, assetPath, "image/webp", null, ct);
            return new StoredImage(key, result.Width, result.Height, result.Size);
        }
        catch (ObjectStoreException)
        {
            throw Errors.UpstreamFailed("store_upload_failed", "Không thể lưu ảnh lên kho lưu trữ. Vui lòng thử lại.");
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
