namespace ComicPlatform.Domain.Catalog;

public enum PackageStatus
{
    Building = 0,
    Ready = 1,
    Published = 2,
    Failed = 3,
    Superseded = 4,
}

/// <summary>One immutable, versioned CPK1 package of a chapter.</summary>
public sealed class ChapterPackage
{
    public const int CurrentFormatVersion = 1;

    private readonly List<ChapterPage> _pages = [];

    private ChapterPackage()
    {
    }

    public ChapterPackage(Guid chapterId, int version, string storageKey, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        ChapterId = chapterId;
        Version = version;
        StorageKey = storageKey;
        FormatVersion = CurrentFormatVersion;
        Status = PackageStatus.Building;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ChapterId { get; private set; }
    public int Version { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public int FormatVersion { get; private set; }
    public long FileSize { get; private set; }
    public int PageCount { get; private set; }
    public string? Checksum { get; private set; }
    public PackageStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public Chapter? Chapter { get; private set; }
    public IReadOnlyCollection<ChapterPage> Pages => _pages;

    public void MarkReady(long fileSize, string checksum, IEnumerable<ChapterPage> pages)
    {
        FileSize = fileSize;
        Checksum = checksum;
        _pages.Clear();
        _pages.AddRange(pages);
        PageCount = _pages.Count;
        Status = PackageStatus.Ready;
    }

    public void MarkFailed() => Status = PackageStatus.Failed;

    public void ResetForRetry()
    {
        _pages.Clear();
        Status = PackageStatus.Building;
    }

    public void MarkPublished(DateTimeOffset now)
    {
        Status = PackageStatus.Published;
        PublishedAt ??= now;
    }

    /// <summary>Unpublished: package stays on the block volume and can be re-published.</summary>
    public void MarkUnpublished() => Status = PackageStatus.Ready;

    public void MarkSuperseded() => Status = PackageStatus.Superseded;
}

public sealed class ChapterPage
{
    public const int SourceNameMaxLength = 255;

    private ChapterPage()
    {
    }

    public ChapterPage(Guid packageId, Guid chapterId, int pageNumber, string fileName, int width, int height, long fileSize, string storagePath, string? sourceName = null)
    {
        Id = Guid.CreateVersion7();
        PackageId = packageId;
        ChapterId = chapterId;
        PageNumber = pageNumber;
        FileName = fileName;
        Width = width;
        Height = height;
        FileSize = fileSize;
        StoragePath = storagePath;
        SourceName = sourceName is { Length: > SourceNameMaxLength } ? "…" + sourceName[^(SourceNameMaxLength - 1)..] : sourceName;
    }

    public Guid Id { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid ChapterId { get; private set; }
    public int PageNumber { get; private set; }
    public string FileName { get; private set; } = null!;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public long FileSize { get; private set; }
    public string StoragePath { get; private set; } = null!;

    /// <summary>Path of the image inside the uploaded archive (display only, never used as a file path).</summary>
    public string? SourceName { get; private set; }

    public ChapterPackage? Package { get; private set; }
}
