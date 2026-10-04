using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Identity;
using ComicPlatform.Domain.Publishing;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Admin;

public sealed record DailyPoint(DateOnly Day, int Count);

public sealed record AdminStatsDto(
    int ActiveHouses,
    int SuspendedHouses,
    int Users,
    int DisabledUsers,
    int Comics,
    int Chapters,
    int PublishedChapters,
    int ProcessingChapters,
    int FailedChapters,
    long PublishedBytes,
    long TotalPackageBytes,
    int PublishedPages,
    int JobsLast24h,
    int FailedJobsLast24h,
    IReadOnlyList<DailyPoint> UploadsPerDay,
    IReadOnlyList<DailyPoint> PublishedPerDay);

public sealed record AuditLogItem(
    Guid Id, DateTimeOffset At, Guid? ActorUserId, string? ActorUsername, string Action, string? TargetType, string? TargetId,
    Guid? HouseId, string? HouseName, string? Ip, string? Data, bool Success);

public sealed class AdminStatsService(IAppDbContext db, TimeProvider clock)
{
    public async Task<AdminStatsDto> GetAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var since30 = now.AddDays(-29).Date;
        var since30Offset = new DateTimeOffset(since30, TimeSpan.Zero);
        var since24h = now.AddHours(-24);

        var comics = db.Comics.IgnoreQueryFilters([QueryFilters.Tenant]);
        var chapters = db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]);
        var packages = db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant]);
        var jobs = db.ProcessingJobs.IgnoreQueryFilters([QueryFilters.Tenant]);

        var chapterStatus = await chapters.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int CountStatus(ChapterStatus s) => chapterStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;

        var uploads = await jobs.Where(j => j.CreatedAt >= since30Offset).Select(j => j.CreatedAt).ToListAsync(ct);
        var published = await chapters.Where(c => c.PublishedAt != null && c.PublishedAt >= since30Offset).Select(c => c.PublishedAt!.Value).ToListAsync(ct);

        return new AdminStatsDto(
            await db.TranslationHouses.CountAsync(h => h.Status == HouseStatus.Active, ct),
            await db.TranslationHouses.CountAsync(h => h.Status == HouseStatus.Suspended, ct),
            await db.Users.CountAsync(ct),
            await db.Users.CountAsync(u => u.Status == UserStatus.Disabled, ct),
            await comics.CountAsync(ct),
            chapterStatus.Sum(x => x.Count),
            CountStatus(ChapterStatus.Published),
            CountStatus(ChapterStatus.Processing),
            CountStatus(ChapterStatus.Failed),
            await packages.Where(p => p.Status == PackageStatus.Published).SumAsync(p => (long?)p.FileSize, ct) ?? 0,
            await packages.Where(p => p.Status != PackageStatus.Failed).SumAsync(p => (long?)p.FileSize, ct) ?? 0,
            await packages.Where(p => p.Status == PackageStatus.Published).SumAsync(p => (int?)p.PageCount, ct) ?? 0,
            await jobs.CountAsync(j => j.CreatedAt >= since24h, ct),
            await jobs.CountAsync(j => j.CreatedAt >= since24h && j.Status == JobStatus.Failed, ct),
            Bucket(uploads, since30),
            Bucket(published, since30));
    }

    public async Task<PagedResult<AuditLogItem>> AuditAsync(string? action, Guid? houseId, int? page, int? pageSize, CancellationToken ct)
    {
        var paging = PageRequest.Normalize(page, pageSize, 50, 200);
        var query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action.StartsWith(action));
        }

        if (houseId is not null)
        {
            query = query.Where(a => a.TranslationHouseId == houseId);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.At).Skip(paging.Skip).Take(paging.PageSize)
            .Select(a => new AuditLogItem(a.Id, a.At, a.ActorUserId,
                db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.Username).FirstOrDefault(),
                a.Action, a.TargetType, a.TargetId, a.TranslationHouseId,
                db.TranslationHouses.Where(h => h.Id == a.TranslationHouseId).Select(h => h.Name).FirstOrDefault(),
                a.Ip, a.Data, a.Success))
            .ToListAsync(ct);

        return new PagedResult<AuditLogItem>(rows, paging.Page, paging.PageSize, total);
    }

    private static List<DailyPoint> Bucket(IEnumerable<DateTimeOffset> times, DateTime since)
    {
        var counts = times.GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime)).ToDictionary(g => g.Key, g => g.Count());
        var start = DateOnly.FromDateTime(since);
        return Enumerable.Range(0, 30).Select(i => start.AddDays(i)).Select(d => new DailyPoint(d, counts.GetValueOrDefault(d))).ToList();
    }
}
