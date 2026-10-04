namespace ComicPlatform.Application.Catalog;

// ---------------- Shared ----------------
public sealed record HouseRef(Guid Id, string Name, string Slug, string? LogoUrl);

public sealed record TagDto(Guid Id, string Name, string Slug);

public sealed record ChapterRef(Guid Id, decimal Number, string? Title, DateTimeOffset? PublishedAt);

// ---------------- Public catalog ----------------
public sealed record ComicCardDto(
    Guid Id,
    string Slug,
    string TranslatedTitle,
    string OriginalTitle,
    string? CoverUrl,
    string Status,
    bool IsMature,
    HouseRef House,
    int ChapterCount,
    DateTimeOffset? LastChapterPublishedAt,
    IReadOnlyList<ChapterRef> LatestChapters);

public sealed record ComicDetailDto(
    Guid Id,
    string Slug,
    string TranslatedTitle,
    string OriginalTitle,
    string? Description,
    string? Author,
    string? Artist,
    string? CoverUrl,
    string Status,
    bool IsMature,
    HouseRef House,
    IReadOnlyList<TagDto> Tags,
    int ChapterCount,
    ChapterRef? FirstChapter,
    ChapterRef? LatestChapter,
    DateTimeOffset? LastChapterPublishedAt,
    DateTimeOffset CreatedAt);

public sealed record PublicChapterDto(Guid Id, decimal Number, string? Title, DateTimeOffset? PublishedAt, int PageCount);

public sealed record ChapterSummaryDto(
    Guid Id,
    Guid ComicId,
    string ComicTitle,
    string ComicSlug,
    bool IsMature,
    decimal Number,
    string? Title,
    DateTimeOffset? PublishedAt,
    int PageCount,
    ChapterRef? Prev,
    ChapterRef? Next,
    HouseRef House);

public sealed record ReaderPackageDto(string Url, int Version, long Size, string Sha256, int PageCount, int Format);

public sealed record ReaderPageDto(int N, int W, int H);

public sealed record ReaderDto(ChapterSummaryDto Chapter, ReaderPackageDto Package, IReadOnlyList<ReaderPageDto> Pages);

public sealed record PublicHouseDto(Guid Id, string Name, string Slug, string? Description, string? LogoUrl, int ComicCount, HouseLinksDto Links, DateTimeOffset CreatedAt);

public sealed record HouseLinksDto(string? Facebook, string? Discord, string? Website);

public sealed record TagWithCountDto(Guid Id, string Name, string Slug, int ComicCount);

// ---------------- House (studio) ----------------
public sealed record SaveComicRequest(
    string? OriginalTitle,
    string? TranslatedTitle,
    string? Slug,
    string? Description,
    string? Author,
    string? Artist,
    string? Status,
    bool? IsMature,
    IReadOnlyList<string>? Tags);

public sealed record HouseComicListItem(
    Guid Id,
    string Slug,
    string TranslatedTitle,
    string OriginalTitle,
    string? CoverUrl,
    string Status,
    bool IsMature,
    int ChapterCount,
    int PublishedChapterCount,
    DateTimeOffset UpdatedAt);

public sealed record HouseComicDetail(
    Guid Id,
    string Slug,
    string TranslatedTitle,
    string OriginalTitle,
    string? Description,
    string? Author,
    string? Artist,
    string? CoverUrl,
    string Status,
    bool IsMature,
    IReadOnlyList<TagDto> Tags,
    int ChapterCount,
    int PublishedChapterCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SaveChapterRequest(decimal? Number, string? Title);

public sealed record JobSummary(
    Guid Id,
    Guid ChapterId,
    string Status,
    string Stage,
    int ProgressDone,
    int ProgressTotal,
    int Attempts,
    string? ErrorCode,
    string? ErrorMessage,
    string? OriginalFileName,
    long UploadSize,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record PackageVersionDto(Guid Id, int Version, string Status, long FileSize, int PageCount, string? Checksum, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

public sealed record HouseChapterListItem(
    Guid Id,
    decimal Number,
    string? Title,
    string Status,
    int PageCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt,
    int? PublishedVersion,
    int? ReadyVersion,
    JobSummary? LatestJob);

public sealed record HouseChapterDetail(
    Guid Id,
    Guid ComicId,
    string ComicTitle,
    decimal Number,
    string? Title,
    string Status,
    int PageCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? PublishedVersion,
    int? ReadyVersion,
    IReadOnlyList<PackageVersionDto> Versions,
    JobSummary? LatestJob,
    ChapterActions Actions);

public sealed record VersionPreviewPage(int N, int W, int H, long Size, string? SourceName);

public sealed record VersionPreviewDto(
    Guid ChapterId,
    Guid ComicId,
    string ComicTitle,
    decimal Number,
    string? Title,
    int Version,
    string Status,
    long FileSize,
    int PageCount,
    string Sha256,
    bool IsPublished,
    bool CanPublish,
    IReadOnlyList<VersionPreviewPage> Pages);

public sealed record ChapterActions(bool CanUpload, bool CanPublish, bool CanUnpublish, bool CanRetry, bool CanDelete);

public sealed record ProcessingJobListItem(
    Guid Id,
    Guid ChapterId,
    Guid ComicId,
    string ComicTitle,
    decimal ChapterNumber,
    string Status,
    string Stage,
    int ProgressDone,
    int ProgressTotal,
    string? ErrorMessage,
    string? OriginalFileName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);

public sealed record HouseDashboardDto(
    int ComicCount,
    int ChapterCount,
    int PublishedChapterCount,
    int ProcessingCount,
    int FailedCount,
    int ReadyCount,
    long StorageBytes,
    int MemberCount,
    IReadOnlyList<ProcessingJobListItem> RecentJobs);
