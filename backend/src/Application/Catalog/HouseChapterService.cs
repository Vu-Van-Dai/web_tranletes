using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Publishing;
using ComicPlatform.Application.Tenancy;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Application.Catalog;

public sealed class HouseChapterService(
    IAppDbContext db,
    ITenantContext tenant,
    TenantResourceGuard guard,
    IChapterFiles files,
    IStoragePaths paths,
    StorageOutbox outbox,
    IAuditLogger audit,
    TimeProvider clock,
    ILogger<HouseChapterService> logger)
{
    public const decimal MaxChapterNumber = 99_999.99m;

    public async Task<IReadOnlyList<HouseChapterListItem>> ListAsync(Guid comicId, CancellationToken ct)
    {
        await guard.GetComicAsync(comicId, ct);
        var chapters = await db.Chapters.AsNoTracking().Where(ch => ch.ComicId == comicId).OrderByDescending(ch => ch.ChapterNumber).ToListAsync(ct);
        var ids = chapters.Select(c => c.Id).ToList();
        var packages = await db.ChapterPackages.AsNoTracking()
            .Where(p => ids.Contains(p.ChapterId))
            .Select(p => new { p.Id, p.ChapterId, p.Version, p.Status })
            .ToListAsync(ct);
        var jobs = await LatestJobsAsync(ids, ct);

        return chapters.Select(ch =>
        {
            var pkgs = packages.Where(p => p.ChapterId == ch.Id).ToList();
            var publishedVersion = pkgs.FirstOrDefault(p => p.Id == ch.PublishedPackageId)?.Version;
            var readyVersion = pkgs.Where(p => p.Status == PackageStatus.Ready && (publishedVersion is null || p.Version > publishedVersion))
                .Select(p => (int?)p.Version).Max();
            return new HouseChapterListItem(ch.Id, ApiText.ChapterNumber(ch.ChapterNumber), ch.Title, ApiText.Enum(ch.Status), ch.PageCount,
                ch.PublishedAt, ch.UpdatedAt, publishedVersion, readyVersion, jobs.GetValueOrDefault(ch.Id));
        }).ToList();
    }

    public async Task<HouseChapterDetail> CreateAsync(Guid comicId, SaveChapterRequest request, CancellationToken ct)
    {
        var comic = await guard.GetComicAsync(comicId, ct);
        var (number, title) = Validate(request);
        await EnsureNumberFreeAsync(comic.Id, number, null, ct);
        var chapter = new Chapter(comic.Id, number, title, clock.GetUtcNow());
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Chapter {ChapterId} ({Number}) created for comic {ComicId}", chapter.Id, number, comicId);
        return await GetAsync(chapter.Id, ct);
    }

    public async Task<HouseChapterDetail> GetAsync(Guid chapterId, CancellationToken ct)
    {
        var chapter = await guard.GetChapterAsync(chapterId, ct);
        var versions = await db.ChapterPackages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId)
            .OrderByDescending(p => p.Version)
            .Select(p => new PackageVersionDto(p.Id, p.Version, p.Status.ToString(), p.FileSize, p.PageCount, p.Checksum, p.CreatedAt, p.PublishedAt))
            .ToListAsync(ct);
        versions = versions.Select(v => v with { Status = char.ToLowerInvariant(v.Status[0]) + v.Status[1..] }).ToList();

        var latestJob = (await LatestJobsAsync([chapterId], ct)).GetValueOrDefault(chapterId);
        var publishedVersion = versions.FirstOrDefault(v => v.Id == chapter.PublishedPackageId)?.Version;
        var readyVersion = versions.Where(v => v.Status == "ready" && (publishedVersion is null || v.Version > publishedVersion)).Select(v => (int?)v.Version).Max();
        var jobActive = latestJob is { Status: "queued" or "running" };

        var actions = new ChapterActions(
            CanUpload: !jobActive,
            CanPublish: !jobActive && readyVersion is not null,
            CanUnpublish: chapter.Status == ChapterStatus.Published,
            CanRetry: !jobActive && latestJob is { Status: "failed" },
            CanDelete: !jobActive && chapter.CanDelete);

        return new HouseChapterDetail(chapter.Id, chapter.ComicId, chapter.Comic!.TranslatedTitle, ApiText.ChapterNumber(chapter.ChapterNumber), chapter.Title,
            ApiText.Enum(chapter.Status), chapter.PageCount, chapter.PublishedAt, chapter.CreatedAt, chapter.UpdatedAt,
            publishedVersion, readyVersion, versions, latestJob, actions);
    }

    /// <summary>
    /// Pages of a processed version exactly as readers will receive them (ready or published versions only),
    /// with the archive entry each page came from, so the house can check order and content before publishing.
    /// </summary>
    public async Task<VersionPreviewDto> PreviewAsync(Guid chapterId, int version, CancellationToken ct)
    {
        var detail = await GetAsync(chapterId, ct); // tenant guard
        var package = await FindPreviewablePackageAsync(chapterId, version, ct);
        var pages = await db.ChapterPages.AsNoTracking()
            .Where(p => p.PackageId == package.Id)
            .OrderBy(p => p.PageNumber)
            .Select(p => new VersionPreviewPage(p.PageNumber, p.Width, p.Height, p.FileSize, p.SourceName))
            .ToListAsync(ct);

        var canPublish = detail.Actions.CanPublish && detail.ReadyVersion == version;
        return new VersionPreviewDto(detail.Id, detail.ComicId, detail.ComicTitle, detail.Number, detail.Title, package.Version,
            ApiText.Enum(package.Status), package.FileSize, package.PageCount, package.Checksum!, package.Status == PackageStatus.Published,
            canPublish, pages);
    }

    /// <summary>Local path of a ready/published package for the house preview (never served publicly).</summary>
    public async Task<string> PreviewPackagePathAsync(Guid chapterId, int version, CancellationToken ct)
    {
        var chapter = await guard.GetChapterAsync(chapterId, ct);
        var package = await FindPreviewablePackageAsync(chapterId, version, ct);
        if (!files.PackageExists(chapter.ComicId, chapterId, package.Version))
        {
            throw Errors.NotFound("Gói chương");
        }

        return paths.PackagePath(chapter.ComicId, chapterId, package.Version);
    }

    private async Task<ChapterPackage> FindPreviewablePackageAsync(Guid chapterId, int version, CancellationToken ct)
    {
        var package = await db.ChapterPackages.AsNoTracking().FirstOrDefaultAsync(p => p.ChapterId == chapterId && p.Version == version, ct);
        if (package is null || package.Status is not (PackageStatus.Ready or PackageStatus.Published) || package.Checksum is null)
        {
            throw Errors.NotFound("Phiên bản");
        }

        return package;
    }

    public async Task<HouseChapterDetail> UpdateAsync(Guid chapterId, SaveChapterRequest request, CancellationToken ct)
    {
        var chapter = await guard.GetChapterAsync(chapterId, ct);
        var (number, title) = Validate(request);
        if (number != chapter.ChapterNumber)
        {
            await EnsureNumberFreeAsync(chapter.ComicId, number, chapter.Id, ct);
        }

        chapter.UpdateDetails(number, title, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await GetAsync(chapterId, ct);
    }

    public async Task DeleteAsync(Guid chapterId, CancellationToken ct)
    {
        var chapter = await guard.GetChapterAsync(chapterId, ct);
        if (await db.ProcessingJobs.AnyAsync(j => j.ChapterId == chapterId && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running), ct))
        {
            throw Errors.Conflict("chapter_processing", "Chương đang được xử lý, vui lòng đợi hoàn tất.");
        }

        if (!chapter.CanDelete)
        {
            throw Errors.Conflict("chapter_published", "Hãy gỡ chương khỏi trang đọc trước khi xoá.");
        }

        var keys = await db.ChapterPackages.Where(p => p.ChapterId == chapterId).Select(p => p.StorageKey).ToListAsync(ct);
        foreach (var key in keys)
        {
            outbox.Withdraw(key, null);
        }

        // Clear the FK first so the cascade order does not matter.
        db.Chapters.Remove(chapter);
        audit.Add(AuditActions.ChapterDeleted, "chapter", chapter.Id, tenant.HouseId, new { chapter.ComicId, Number = chapter.ChapterNumber });
        await db.SaveChangesAsync(ct);
        files.DeleteChapter(chapter.ComicId, chapter.Id);
        logger.LogInformation("Chapter {ChapterId} deleted", chapterId);
    }

    public async Task<JobSummary?> GetLatestJobAsync(Guid chapterId, CancellationToken ct)
    {
        await guard.GetChapterAsync(chapterId, ct);
        return (await LatestJobsAsync([chapterId], ct)).GetValueOrDefault(chapterId);
    }

    public async Task<IReadOnlyList<ProcessingJobListItem>> ListRecentJobsAsync(int take, CancellationToken ct)
    {
        var rows = await db.ProcessingJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(j => new
            {
                j.Id, j.ChapterId, j.Chapter!.ComicId, ComicTitle = j.Chapter.Comic!.TranslatedTitle, j.Chapter.ChapterNumber,
                j.Status, j.Stage, j.ProgressDone, j.ProgressTotal, j.ErrorMessage, j.OriginalFileName, j.CreatedAt, j.FinishedAt,
            })
            .ToListAsync(ct);

        return rows.Select(j => new ProcessingJobListItem(j.Id, j.ChapterId, j.ComicId, j.ComicTitle, ApiText.ChapterNumber(j.ChapterNumber),
            ApiText.Enum(j.Status), ApiText.Enum(j.Stage), j.ProgressDone, j.ProgressTotal, j.ErrorMessage, j.OriginalFileName, j.CreatedAt, j.FinishedAt)).ToList();
    }

    public async Task<HouseDashboardDto> DashboardAsync(CancellationToken ct)
    {
        var statusCounts = await db.Chapters.AsNoTracking().GroupBy(ch => ch.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(ChapterStatus s) => statusCounts.FirstOrDefault(x => x.Key == s)?.Count ?? 0;

        var comicCount = await db.Comics.CountAsync(ct);
        var storage = await db.ChapterPackages.Where(p => p.Status != PackageStatus.Failed).SumAsync(p => (long?)p.FileSize, ct) ?? 0;
        var members = await db.TranslationHouseMembers.CountAsync(m => m.TranslationHouseId == tenant.HouseId && m.Status == Domain.Tenancy.MemberStatus.Active, ct);
        var activeJobs = await db.ProcessingJobs.CountAsync(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running, ct);

        return new HouseDashboardDto(comicCount, statusCounts.Sum(x => x.Count), Count(ChapterStatus.Published), activeJobs,
            Count(ChapterStatus.Failed), Count(ChapterStatus.Ready), storage, members, await ListRecentJobsAsync(8, ct));
    }

    internal async Task<Dictionary<Guid, JobSummary>> LatestJobsAsync(IReadOnlyCollection<Guid> chapterIds, CancellationToken ct)
    {
        var jobs = await db.ProcessingJobs.AsNoTracking()
            .Where(j => chapterIds.Contains(j.ChapterId))
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);

        return jobs.GroupBy(j => j.ChapterId).ToDictionary(g => g.Key, g => ToSummary(g.First()));
    }

    public static JobSummary ToSummary(ProcessingJob j) => new(
        j.Id, j.ChapterId, ApiText.Enum(j.Status), ApiText.Enum(j.Stage), j.ProgressDone, j.ProgressTotal, j.Attempts,
        j.ErrorCode, j.ErrorMessage, j.OriginalFileName, j.UploadSize, j.CreatedAt, j.StartedAt, j.FinishedAt);

    private static (decimal Number, string? Title) Validate(SaveChapterRequest r)
    {
        var v = new Validator();
        if (r.Number is null)
        {
            v.Add("number", "Số chương không được để trống.");
        }
        else
        {
            v.Range("number", r.Number.Value, "Số chương", 0, MaxChapterNumber);
            v.Must(decimal.Round(r.Number.Value, 2) == r.Number.Value, "number", "Số chương chỉ được có tối đa 2 chữ số thập phân.");
        }

        v.MaxLength("title", r.Title, "Tên chương", 200);
        v.ThrowIfInvalid();
        return (r.Number!.Value, string.IsNullOrWhiteSpace(r.Title) ? null : r.Title.Trim());
    }

    private async Task EnsureNumberFreeAsync(Guid comicId, decimal number, Guid? exclude, CancellationToken ct)
    {
        if (await db.Chapters.AnyAsync(ch => ch.ComicId == comicId && ch.ChapterNumber == number && ch.Id != exclude, ct))
        {
            throw Errors.Field("number", $"Chương {ApiText.ChapterNumber(number)} đã tồn tại.");
        }
    }
}
