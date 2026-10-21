using ComicPlatform.Domain.Identity;
using ComicPlatform.Domain.Tenancy;

namespace ComicPlatform.Application.Abstractions;

/// <summary>The authenticated caller, as established by the JWT (only the subject is trusted).</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    string? IpAddress { get; }
}

/// <summary>House (tenant) context resolved from DB memberships for /api/translation-house requests.</summary>
public interface ITenantContext
{
    bool IsResolved { get; }
    Guid HouseId { get; }
    Guid UserId { get; }
    Guid MemberId { get; }
    HouseRole Role { get; }
    HousePermissions Permissions { get; }
}

public sealed class TenantContext : ITenantContext
{
    public bool IsResolved { get; private set; }
    public Guid HouseId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid MemberId { get; private set; }
    public HouseRole Role { get; private set; }
    public HousePermissions Permissions { get; private set; }

    public void Set(Guid houseId, Guid userId, Guid memberId, HouseRole role, HousePermissions permissions)
    {
        if (IsResolved)
        {
            throw new InvalidOperationException("Tenant context is already resolved for this scope.");
        }

        HouseId = houseId;
        UserId = userId;
        MemberId = memberId;
        Role = role;
        Permissions = permissions;
        IsResolved = true;
    }
}

public enum PasswordCheck
{
    Failed,
    Success,
    SuccessRehashNeeded,
}

public interface IPasswordHasher
{
    Task<string> HashAsync(string password, CancellationToken ct = default);
    Task<PasswordCheck> VerifyAsync(string hash, string password, CancellationToken ct = default);

    /// <summary>Burns comparable CPU for unknown users so timing does not reveal account existence.</summary>
    Task DummyVerifyAsync(string password, CancellationToken ct = default);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
    string CreateOpaqueToken();
    string HashOpaqueToken(string token);
    TimeSpan RefreshTokenLifetime { get; }
}

public sealed record StoredObjectInfo(long Size, string? Sha256);

/// <summary>Any transport/storage failure of an <see cref="IObjectStore"/> implementation.</summary>
public sealed class ObjectStoreException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>Object storage for public assets (R2 in production, a local directory in dev/E2E).</summary>
public interface IObjectStore
{
    string Name { get; }
    Task PutFileAsync(string key, string filePath, string contentType, string? sha256, CancellationToken ct);
    Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>Optional edge-cache purge (Cloudflare API); no-op when not configured.</summary>
public interface ICdnPurger
{
    bool IsEnabled { get; }
    Task PurgeAsync(IReadOnlyCollection<string> urls, CancellationToken ct);
}

public interface IPublicUrls
{
    /// <summary>Public URL of an object key (package, cover, logo), or null.</summary>
    string? ObjectUrl(string? key);
}

/// <summary>Block-volume layout. All paths are built from GUIDs/ints only — never from user input.</summary>
public interface IStoragePaths
{
    string ComicsRoot { get; }
    string TempRoot { get; }
    string ChapterVersionDir(Guid comicId, Guid chapterId, int version);
    string PagesDir(Guid comicId, Guid chapterId, int version);
    string PackagePath(Guid comicId, Guid chapterId, int version);
    string ChapterDir(Guid comicId, Guid chapterId);
    string ComicDir(Guid comicId);
    string AssetPath(string objectKey);
    string UploadPath(Guid jobId);
    string ExtraUploadPath(Guid uploadId, int index);
    string WorkDir(Guid jobId);
}

public static class ObjectKeys
{
    public static string Package(Guid comicId, Guid chapterId, int version) =>
        $"comics/{comicId:D}/chapters/{chapterId:D}/v{version}.pkg";

    public static string Cover(Guid comicId, string hash16) => $"comics/{comicId:D}/cover/{hash16}.webp";

    public static string Logo(Guid houseId, string hash16) => $"houses/{houseId:D}/logo/{hash16}.webp";

    public static string HouseCover(Guid houseId, string hash16) => $"houses/{houseId:D}/cover/{hash16}.webp";

    public static string Ad(string hash16) => $"ads/{hash16}.webp";
}

public interface IProcessingQueue
{
    bool TryEnqueue(Guid jobId);
    int Count { get; }
    int Capacity { get; }
}

/// <summary>Cross-request mutual exclusion backed by PostgreSQL advisory locks.</summary>
public interface IDistributedLock
{
    Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken ct);
}

public sealed record StoredImage(string Key, int Width, int Height, long Size);

/// <summary>Cover / logo / banner pipeline: validate → WebP thumbnail → block volume → object store.</summary>
public interface IMediaService
{
    Task<StoredImage> StoreCoverAsync(Guid comicId, Stream input, CancellationToken ct);
    Task<StoredImage> StoreLogoAsync(Guid houseId, Stream input, CancellationToken ct);
    Task<StoredImage> StoreHouseCoverAsync(Guid houseId, Stream input, CancellationToken ct);
    Task<StoredImage> StoreAdImageAsync(Stream input, CancellationToken ct);
}

/// <summary>Deletes block-volume files of chapters/comics (best effort, logged).</summary>
public interface IChapterFiles
{
    void DeleteChapter(Guid comicId, Guid chapterId);
    void DeleteVersion(Guid comicId, Guid chapterId, int version);
    void DeleteComic(Guid comicId);
    bool PackageExists(Guid comicId, Guid chapterId, int version);
}

public sealed record SavedUpload(string Path, long Size);

/// <summary>Streams an uploaded archive to the temp area with a hard byte cap.</summary>
public interface IUploadStore
{
    Task<SavedUpload> SaveAsync(Guid uploadId, Stream content, long maxBytes, CancellationToken ct);

    /// <summary>Saves an extra page image (JPEG/PNG/static WebP, checked by magic bytes) next to an upload.</summary>
    Task<SavedUpload> SaveImageAsync(Guid uploadId, int index, Stream content, long maxBytes, CancellationToken ct);
    bool Exists(string path);
    void Delete(string path);
}
