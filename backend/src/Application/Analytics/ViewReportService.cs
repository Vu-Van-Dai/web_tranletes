using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Settings;
using ComicPlatform.Domain.Analytics;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Analytics;

public sealed record ViewTotalsDto(long Views, long SuspiciousViews, decimal? Revenue);

public sealed record ChapterStatsDto(Guid ChapterId, decimal? Number, string? Title, bool Deleted, long Views, long SuspiciousViews, decimal? Revenue);

public sealed record ComicStatsDto(Guid ComicId, string Title, bool Deleted, long Views, long SuspiciousViews, decimal? Revenue, IReadOnlyList<ChapterStatsDto> Chapters);

public sealed record DailyStatsDto(DateOnly Day, long Views, long SuspiciousViews, decimal? Revenue);

/// <summary>Per-house report: series totals are the sums of their chapters. Revenue is null for non-owners.</summary>
public sealed record HouseViewReportDto(
    DateOnly From,
    DateOnly To,
    bool RevenueVisible,
    decimal? RevenuePerThousandViews,
    ViewTotalsDto Totals,
    IReadOnlyList<ComicStatsDto> Comics,
    IReadOnlyList<DailyStatsDto> Daily);

public sealed record HouseRevenueRowDto(Guid HouseId, string Name, string? Slug, long Views, long SuspiciousViews, decimal Revenue, double SuspiciousRatio);

public sealed record RejectionDto(string Reason, long Count);

/// <summary>Platform report for payouts: one row per house, plus the reasons views were rejected.</summary>
public sealed record AdminRevenueReportDto(
    DateOnly From,
    DateOnly To,
    decimal RevenuePerThousandViews,
    ViewTotalsDto Totals,
    IReadOnlyList<HouseRevenueRowDto> Houses,
    IReadOnlyList<DailyStatsDto> Daily,
    IReadOnlyList<RejectionDto> Rejections);

public sealed class ViewReportService(IAppDbContext db, ITenantContext tenant, SettingsService settings, TimeProvider clock)
{
    private const int MaxRangeDays = 366;

    /// <summary>The current house's views (all members) and revenue (owners only).</summary>
    public async Task<HouseViewReportDto> HouseReportAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (start, end) = Range(from, to);
        var revenueVisible = tenant.Role == HouseRole.Owner;
        var rows = db.ChapterViewDaily.AsNoTracking().Where(x => x.Day >= start && x.Day <= end);
        var rate = revenueVisible ? (await settings.GetAsync(ct)).RevenuePerThousandViews : (decimal?)null;
        return await BuildAsync(rows, start, end, revenueVisible, rate, ct);
    }

    public async Task<HouseViewReportDto> AdminHouseReportAsync(Guid houseId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (start, end) = Range(from, to);
        if (!await db.TranslationHouses.AnyAsync(h => h.Id == houseId, ct))
        {
            throw Errors.NotFound("Nhóm dịch");
        }

        var rows = db.ChapterViewDaily.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(x => x.TranslationHouseId == houseId && x.Day >= start && x.Day <= end);
        return await BuildAsync(rows, start, end, true, (await settings.GetAsync(ct)).RevenuePerThousandViews, ct);
    }

    public async Task<AdminRevenueReportDto> AdminReportAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (start, end) = Range(from, to);
        var rows = db.ChapterViewDaily.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().Where(x => x.Day >= start && x.Day <= end);
        var perHouse = await rows.GroupBy(x => x.TranslationHouseId)
            .Select(g => new { HouseId = g.Key, Views = g.Sum(x => x.Views), Suspicious = g.Sum(x => x.SuspiciousViews), Revenue = g.Sum(x => x.Revenue) })
            .ToListAsync(ct);
        var ids = perHouse.Select(h => h.HouseId).ToList();
        var houses = await db.TranslationHouses.AsNoTracking().Where(h => ids.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => new { h.Name, h.Slug }, ct);
        var daily = await Daily(rows, true, ct);
        var rejections = await db.ViewRejectionDaily.AsNoTracking()
            .Where(r => r.Day >= start && r.Day <= end)
            .GroupBy(r => r.Reason)
            .Select(g => new RejectionDto(g.Key, g.Sum(r => r.Count)))
            .ToListAsync(ct);

        var houseRows = perHouse
            .Select(h => new HouseRevenueRowDto(
                h.HouseId,
                houses.TryGetValue(h.HouseId, out var info) ? info.Name : "(nhóm đã xoá)",
                houses.TryGetValue(h.HouseId, out var info2) ? info2.Slug : null,
                h.Views,
                h.Suspicious,
                decimal.Round(h.Revenue, 2),
                Ratio(h.Views, h.Suspicious)))
            .OrderByDescending(h => h.Revenue).ThenByDescending(h => h.Views)
            .ToList();
        var totals = new ViewTotalsDto(perHouse.Sum(h => h.Views), perHouse.Sum(h => h.Suspicious), decimal.Round(perHouse.Sum(h => h.Revenue), 2));
        return new AdminRevenueReportDto(start, end, (await settings.GetAsync(ct)).RevenuePerThousandViews, totals, houseRows, daily,
            rejections.OrderByDescending(r => r.Count).ToList());
    }

    private async Task<HouseViewReportDto> BuildAsync(IQueryable<ChapterViewDaily> rows, DateOnly start, DateOnly end, bool revenueVisible, decimal? rate, CancellationToken ct)
    {
        var perChapter = await rows.GroupBy(x => new { x.ChapterId, x.ComicId })
            .Select(g => new { g.Key.ChapterId, g.Key.ComicId, Views = g.Sum(x => x.Views), Suspicious = g.Sum(x => x.SuspiciousViews), Revenue = g.Sum(x => x.Revenue) })
            .ToListAsync(ct);

        var chapterIds = perChapter.Select(c => c.ChapterId).ToList();
        var comicIds = perChapter.Select(c => c.ComicId).Distinct().ToList();
        var chapters = await db.Chapters.IgnoreQueryFilters().AsNoTracking()
            .Where(c => chapterIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ChapterNumber, c.Title })
            .ToDictionaryAsync(c => c.Id, ct);
        var comics = await db.Comics.IgnoreQueryFilters().AsNoTracking()
            .Where(c => comicIds.Contains(c.Id))
            .Select(c => new { c.Id, c.TranslatedTitle, Deleted = c.DeletedAt != null })
            .ToDictionaryAsync(c => c.Id, ct);

        decimal? Money(decimal value) => revenueVisible ? decimal.Round(value, 2) : null;

        var comicRows = perChapter
            .GroupBy(c => c.ComicId)
            .Select(g =>
            {
                var chapterRows = g
                    .Select(c => chapters.TryGetValue(c.ChapterId, out var ch)
                        ? new ChapterStatsDto(c.ChapterId, ApiText.ChapterNumber(ch.ChapterNumber), ch.Title, false, c.Views, c.Suspicious, Money(c.Revenue))
                        : new ChapterStatsDto(c.ChapterId, null, null, true, c.Views, c.Suspicious, Money(c.Revenue)))
                    .OrderByDescending(c => c.Number ?? -1)
                    .ToList();
                var comic = comics.GetValueOrDefault(g.Key);
                return new ComicStatsDto(
                    g.Key,
                    comic?.TranslatedTitle ?? "(truyện đã xoá)",
                    comic is null || comic.Deleted,
                    g.Sum(c => c.Views),
                    g.Sum(c => c.Suspicious),
                    Money(g.Sum(c => c.Revenue)),
                    chapterRows);
            })
            .OrderByDescending(c => c.Views)
            .ToList();

        var totals = new ViewTotalsDto(perChapter.Sum(c => c.Views), perChapter.Sum(c => c.Suspicious), Money(perChapter.Sum(c => c.Revenue)));
        return new HouseViewReportDto(start, end, revenueVisible, rate, totals, comicRows, await Daily(rows, revenueVisible, ct));
    }

    private static async Task<IReadOnlyList<DailyStatsDto>> Daily(IQueryable<ChapterViewDaily> rows, bool revenueVisible, CancellationToken ct)
    {
        var daily = await rows.GroupBy(x => x.Day)
            .Select(g => new { Day = g.Key, Views = g.Sum(x => x.Views), Suspicious = g.Sum(x => x.SuspiciousViews), Revenue = g.Sum(x => x.Revenue) })
            .OrderBy(x => x.Day)
            .ToListAsync(ct);
        return daily.Select(d => new DailyStatsDto(d.Day, d.Views, d.Suspicious, revenueVisible ? decimal.Round(d.Revenue, 2) : null)).ToList();
    }

    /// <summary>Defaults to the current month (Vietnam time); at most one year.</summary>
    private (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var today = ReportingDay.From(clock.GetUtcNow());
        var end = to ?? today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (start > end)
        {
            throw Errors.BadRequest("invalid_range", "Ngày bắt đầu phải trước ngày kết thúc.");
        }

        if (end.DayNumber - start.DayNumber >= MaxRangeDays)
        {
            throw Errors.BadRequest("invalid_range", "Khoảng thời gian tối đa là 1 năm.");
        }

        return (start, end);
    }

    private static double Ratio(long views, long suspicious) =>
        views + suspicious == 0 ? 0 : Math.Round((double)suspicious / (views + suspicious), 4);
}
