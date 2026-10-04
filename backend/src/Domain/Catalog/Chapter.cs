using ComicPlatform.Domain.Common;

namespace ComicPlatform.Domain.Catalog;

public enum ChapterStatus
{
    /// <summary>Created, no content uploaded yet.</summary>
    Draft = 0,

    /// <summary>A processing job is queued/running and no version is public.</summary>
    Processing = 1,

    /// <summary>A package is built (on the block volume) and waiting to be published.</summary>
    Ready = 2,

    /// <summary>Public. A newer version may be processing or ready in the background.</summary>
    Published = 3,

    /// <summary>Was public, withdrawn by the house. Its last package can be re-published.</summary>
    Unpublished = 4,

    /// <summary>The last processing attempt failed and nothing is public.</summary>
    Failed = 5,
}

/// <summary>
/// Chapter aggregate root. All lifecycle transitions go through these methods so the
/// state machine is enforced in one place (and unit-tested).
/// </summary>
public sealed class Chapter
{
    private Chapter()
    {
    }

    public Chapter(Guid comicId, decimal chapterNumber, string? title, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        ComicId = comicId;
        ChapterNumber = chapterNumber;
        Title = title;
        Status = ChapterStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ComicId { get; private set; }
    public decimal ChapterNumber { get; private set; }
    public string? Title { get; private set; }
    public ChapterStatus Status { get; private set; }
    public Guid? PublishedPackageId { get; private set; }
    public int LatestPackageVersion { get; private set; }
    public int PageCount { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public Comic? Comic { get; private set; }

    public bool IsPublished => Status == ChapterStatus.Published;

    public bool CanDelete => Status is not (ChapterStatus.Published or ChapterStatus.Processing);

    public void UpdateDetails(decimal chapterNumber, string? title, DateTimeOffset now)
    {
        ChapterNumber = chapterNumber;
        Title = title;
        UpdatedAt = now;
    }

    /// <summary>Starts processing a new upload and returns the new package version number.</summary>
    public int BeginProcessing(DateTimeOffset now)
    {
        if (Status == ChapterStatus.Processing)
        {
            throw new DomainException("chapter_processing", "Chương đang được xử lý, vui lòng đợi hoàn tất.");
        }

        // A published chapter stays public while the new version is processed.
        if (Status != ChapterStatus.Published)
        {
            Status = ChapterStatus.Processing;
        }

        LatestPackageVersion++;
        UpdatedAt = now;
        return LatestPackageVersion;
    }

    public void CompleteProcessing(DateTimeOffset now)
    {
        if (Status == ChapterStatus.Processing)
        {
            Status = ChapterStatus.Ready;
        }

        UpdatedAt = now;
    }

    public void FailProcessing(DateTimeOffset now)
    {
        if (Status == ChapterStatus.Processing)
        {
            Status = ChapterStatus.Failed;
        }

        UpdatedAt = now;
    }

    /// <summary>Re-queues a failed job (retry).</summary>
    public void RetryProcessing(DateTimeOffset now)
    {
        if (Status == ChapterStatus.Failed)
        {
            Status = ChapterStatus.Processing;
        }

        UpdatedAt = now;
    }

    public void Publish(Guid packageId, int pageCount, DateTimeOffset now)
    {
        if (Status is ChapterStatus.Draft or ChapterStatus.Processing or ChapterStatus.Failed)
        {
            throw new DomainException("chapter_not_ready", "Chương chưa có bản đã xử lý xong để đăng.");
        }

        if (Status == ChapterStatus.Published && PublishedPackageId == packageId)
        {
            throw new DomainException("chapter_already_published", "Phiên bản này đã được đăng.");
        }

        PublishedPackageId = packageId;
        PageCount = pageCount;
        Status = ChapterStatus.Published;
        PublishedAt ??= now;
        UpdatedAt = now;
    }

    public void Unpublish(DateTimeOffset now)
    {
        if (Status != ChapterStatus.Published)
        {
            throw new DomainException("chapter_not_published", "Chương chưa được đăng.");
        }

        Status = ChapterStatus.Unpublished;
        PublishedPackageId = null;
        UpdatedAt = now;
    }
}
