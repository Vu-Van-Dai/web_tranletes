using System.Globalization;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure.Configuration;

namespace ComicPlatform.Infrastructure.Storage;

/// <summary>
/// Block volume layout:
/// <code>
/// {COMIC_STORAGE_PATH}/{comicId}/chapters/{chapterId}/v{n}/pages/001.webp
/// {COMIC_STORAGE_PATH}/{comicId}/chapters/{chapterId}/v{n}/chapter.pkg
/// {COMIC_STORAGE_PATH}/{comicId}/cover/{hash}.webp
/// {COMIC_STORAGE_PATH}/_houses/{houseId}/logo/{hash}.webp,  {COMIC_STORAGE_PATH}/_houses/{houseId}/cover/{hash}.webp
/// {COMIC_STORAGE_PATH}/_ads/{hash}.webp
/// {TEMP_STORAGE_PATH}/uploads/{jobId}.zip (+ {jobId}.extra{n}.img),  {TEMP_STORAGE_PATH}/work/{jobId}/
/// </code>
/// Every segment comes from a GUID or an integer; user-supplied names never reach the file system.
/// </summary>
public sealed class StoragePaths : IStoragePaths
{
    public StoragePaths(StorageSettings settings)
    {
        ComicsRoot = Path.GetFullPath(settings.ComicStoragePath);
        TempRoot = Path.GetFullPath(settings.TempStoragePath);
        LocalObjectStoreRoot = Path.GetFullPath(Path.Combine(TempRoot, "..", "object-store"));
    }

    public string ComicsRoot { get; }
    public string TempRoot { get; }

    /// <summary>Directory acting as "R2" when PACKAGE_STORE=local.</summary>
    public string LocalObjectStoreRoot { get; }

    public string ComicDir(Guid comicId) => Combine(ComicsRoot, Id(comicId));

    public string ChapterDir(Guid comicId, Guid chapterId) => Combine(ComicDir(comicId), "chapters", Id(chapterId));

    public string ChapterVersionDir(Guid comicId, Guid chapterId, int version) =>
        Combine(ChapterDir(comicId, chapterId), "v" + version.ToString(CultureInfo.InvariantCulture));

    public string PagesDir(Guid comicId, Guid chapterId, int version) => Combine(ChapterVersionDir(comicId, chapterId, version), "pages");

    public string PackagePath(Guid comicId, Guid chapterId, int version) => Combine(ChapterVersionDir(comicId, chapterId, version), "chapter.pkg");

    public string UploadPath(Guid jobId) => Combine(TempRoot, "uploads", Id(jobId) + ".zip");

    public string ExtraUploadPath(Guid uploadId, int index) =>
        Combine(TempRoot, "uploads", Id(uploadId) + ".extra" + index.ToString(CultureInfo.InvariantCulture) + ".img");

    public string WorkDir(Guid jobId) => Combine(TempRoot, "work", Id(jobId));

    /// <summary>Block-volume file backing a (validated) object key.</summary>
    public string AssetPath(string objectKey)
    {
        if (!ObjectKeyParser.TryParse(objectKey, out var key))
        {
            throw new ArgumentException("Invalid object key.", nameof(objectKey));
        }

        return key.Kind switch
        {
            ObjectKind.Package => PackagePath(key.OwnerId, key.ChapterId!.Value, key.Version!.Value),
            ObjectKind.Cover => Combine(ComicDir(key.OwnerId), "cover", key.Hash + ".webp"),
            ObjectKind.Logo => Combine(ComicsRoot, "_houses", Id(key.OwnerId), "logo", key.Hash + ".webp"),
            ObjectKind.HouseCover => Combine(ComicsRoot, "_houses", Id(key.OwnerId), "cover", key.Hash + ".webp"),
            ObjectKind.Ad => Combine(ComicsRoot, "_ads", key.Hash + ".webp"),
            _ => throw new ArgumentOutOfRangeException(nameof(objectKey)),
        };
    }

    public string LocalObjectPath(string objectKey)
    {
        if (!ObjectKeyParser.TryParse(objectKey, out _))
        {
            throw new ArgumentException("Invalid object key.", nameof(objectKey));
        }

        return Combine(LocalObjectStoreRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
    }

    public void EnsureBaseDirectories()
    {
        Directory.CreateDirectory(ComicsRoot);
        Directory.CreateDirectory(Path.Combine(TempRoot, "uploads"));
        Directory.CreateDirectory(Path.Combine(TempRoot, "work"));
    }

    private static string Id(Guid id) => id.ToString("D");

    /// <summary>Combines and asserts the result stays under <paramref name="root"/> (defense in depth).</summary>
    private static string Combine(string root, params string[] parts)
    {
        var full = Path.GetFullPath(Path.Combine([root, .. parts]));
        var rootFull = Path.GetFullPath(root);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path escapes the storage root.");
        }

        return full;
    }
}

public sealed class PublicUrls(StorageSettings settings) : IPublicUrls
{
    public string? ObjectUrl(string? key) =>
        string.IsNullOrEmpty(key) ? null : $"{settings.PublicImageUrl}/{key}";
}
