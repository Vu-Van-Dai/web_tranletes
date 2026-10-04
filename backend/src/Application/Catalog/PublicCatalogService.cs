using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Common;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Application.Catalog;

/// <summary>
/// Anonymous, cross-house read model. Opts out of the tenant filter explicitly and applies the public
/// visibility rules in exactly one place: house active, comic not deleted (soft-delete filter stays on),
/// at least one published chapter; chapters must be Published.
/// </summary>
public sealed class PublicCatalogService(IAppDbContext db, IPublicUrls urls)
{
    private IQueryable<Comic> VisibleComics =>
        db.Comics.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(c => c.TranslationHouse!.Status == HouseStatus.Active && c.PublishedChapterCount > 0);

    private IQueryable<Chapter> VisibleChapters =>
        db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(ch => ch.Status == ChapterStatus.Published
                         && ch.Comic!.DeletedAt == null
                         && ch.Comic.TranslationHouse!.Status == HouseStatus.Active);

    public async Task<PagedResult<ComicCardDto>> ListComicsAsync(
        string? q, string? tag, string? house, string? status, string? sort, int? page, int? pageSize, CancellationToken ct)
    {
        var paging = PageRequest.Normalize(page, pageSize, 24, 48);
        var query = VisibleComics;

        if (!string.IsNullOrWhiteSpace(q))
        {
            var key = TextNormalizer.ToSearchKey(q.Length > 100 ? q[..100] : q);
            if (key.Length > 0)
            {
                query = query.Where(c => EF.Functions.ILike(c.SearchKey, "%" + HouseComicService.EscapeLike(key) + "%", "\\"));
            }
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagSlug = tag.Trim().ToLowerInvariant();
            query = query.Where(c => c.Tags.Any(t => t.Tag!.Slug == tagSlug));
        }

        if (!string.IsNullOrWhiteSpace(house))
        {
            var houseSlug = house.Trim().ToLowerInvariant();
            query = query.Where(c => c.TranslationHouse!.Slug == houseSlug);
        }

        if (ApiText.TryParseEnum<ComicStatus>(status, out var st))
        {
            query = query.Where(c => c.Status == st);
        }

        query = sort switch
        {
            "new" => query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id),
            "az" => query.OrderBy(c => EF.Functions.Collate(c.TranslatedTitle, "vi-x-icu")).ThenBy(c => c.Id),
            "chapters" => query.OrderByDescending(c => c.PublishedChapterCount).ThenBy(c => c.Id),
            _ => query.OrderByDescending(c => c.LastChapterPublishedAt).ThenBy(c => c.Id),
        };

        var total = await query.CountAsync(ct);
        var rows = await ProjectCards(query.Skip(paging.Skip).Take(paging.PageSize)).ToListAsync(ct);
        return new PagedResult<ComicCardDto>(rows.Select(MapCard).ToList(), paging.Page, paging.PageSize, total);
    }

    public async Task<ComicDetailDto> GetComicAsync(Guid comicId, CancellationToken ct)
    {
        var c = await VisibleComics.Where(x => x.Id == comicId)
            .Select(x => new
            {
                x.Id, x.Slug, x.TranslatedTitle, x.OriginalTitle, x.Description, x.Author, x.Artist, x.CoverKey, x.Status, x.IsMature,
                x.PublishedChapterCount, x.LastChapterPublishedAt, x.CreatedAt,
                House = new { x.TranslationHouse!.Id, x.TranslationHouse.Name, x.TranslationHouse.Slug, x.TranslationHouse.LogoKey },
                Tags = x.Tags.Select(t => new TagDto(t.Tag!.Id, t.Tag.Name, t.Tag.Slug)).ToList(),
            })
            .FirstOrDefaultAsync(ct) ?? throw Errors.NotFound("Truyện");

        var chapters = VisibleChapters.Where(ch => ch.ComicId == comicId);
        var first = await chapters.OrderBy(ch => ch.ChapterNumber).Select(ch => new { ch.Id, ch.ChapterNumber, ch.Title, ch.PublishedAt }).FirstOrDefaultAsync(ct);
        var latest = await chapters.OrderByDescending(ch => ch.ChapterNumber).Select(ch => new { ch.Id, ch.ChapterNumber, ch.Title, ch.PublishedAt }).FirstOrDefaultAsync(ct);

        return new ComicDetailDto(c.Id, c.Slug, c.TranslatedTitle, c.OriginalTitle, c.Description, c.Author, c.Artist, urls.ObjectUrl(c.CoverKey),
            ApiText.Enum(c.Status), c.IsMature, new HouseRef(c.House.Id, c.House.Name, c.House.Slug, urls.ObjectUrl(c.House.LogoKey)),
            c.Tags.OrderBy(t => t.Name).ToList(), c.PublishedChapterCount,
            first is null ? null : new ChapterRef(first.Id, ApiText.ChapterNumber(first.ChapterNumber), first.Title, first.PublishedAt),
            latest is null ? null : new ChapterRef(latest.Id, ApiText.ChapterNumber(latest.ChapterNumber), latest.Title, latest.PublishedAt),
            c.LastChapterPublishedAt, c.CreatedAt);
    }

    public async Task<IReadOnlyList<PublicChapterDto>> ListChaptersAsync(Guid comicId, CancellationToken ct)
    {
        if (!await VisibleComics.AnyAsync(c => c.Id == comicId, ct))
        {
            throw Errors.NotFound("Truyện");
        }

        var rows = await VisibleChapters.Where(ch => ch.ComicId == comicId)
            .OrderByDescending(ch => ch.ChapterNumber)
            .Select(ch => new { ch.Id, ch.ChapterNumber, ch.Title, ch.PublishedAt, ch.PageCount })
            .ToListAsync(ct);
        return rows.Select(r => new PublicChapterDto(r.Id, ApiText.ChapterNumber(r.ChapterNumber), r.Title, r.PublishedAt, r.PageCount)).ToList();
    }

    public async Task<ChapterSummaryDto> GetChapterAsync(Guid chapterId, CancellationToken ct) =>
        (await LoadChapterAsync(chapterId, ct)).Summary;

    public async Task<ReaderDto> GetReaderAsync(Guid chapterId, CancellationToken ct)
    {
        var (summary, packageId) = await LoadChapterAsync(chapterId, ct);
        var package = await db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(p => p.Id == packageId && p.Status == PackageStatus.Published)
            .Select(p => new { p.Id, p.Version, p.StorageKey, p.FileSize, p.Checksum, p.PageCount, p.FormatVersion })
            .FirstOrDefaultAsync(ct) ?? throw Errors.NotFound("Chương");

        var pages = await db.ChapterPages.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(p => p.PackageId == package.Id)
            .OrderBy(p => p.PageNumber)
            .Select(p => new ReaderPageDto(p.PageNumber, p.Width, p.Height))
            .ToListAsync(ct);

        return new ReaderDto(summary,
            new ReaderPackageDto(urls.ObjectUrl(package.StorageKey)!, package.Version, package.FileSize, package.Checksum!, package.PageCount, package.FormatVersion),
            pages);
    }

    public async Task<IReadOnlyList<TagWithCountDto>> ListTagsAsync(CancellationToken ct)
    {
        var visible = VisibleComics;
        var rows = await db.Tags.AsNoTracking()
            .Select(t => new { t.Id, t.Name, t.Slug, Count = visible.Count(c => c.Tags.Any(ct2 => ct2.TagId == t.Id)) })
            .Where(t => t.Count > 0)
            .ToListAsync(ct);
        return rows.OrderBy(t => t.Name, StringComparer.Create(new System.Globalization.CultureInfo("vi-VN"), true))
            .Select(t => new TagWithCountDto(t.Id, t.Name, t.Slug, t.Count)).ToList();
    }

    public async Task<IReadOnlyList<PublicHouseDto>> ListHousesAsync(CancellationToken ct)
    {
        var visible = VisibleComics;
        var rows = await db.TranslationHouses.AsNoTracking()
            .Where(h => h.Status == HouseStatus.Active)
            .Select(h => new { House = h, Count = visible.Count(c => c.TranslationHouseId == h.Id) })
            .OrderByDescending(x => x.Count).ThenBy(x => x.House.Name)
            .ToListAsync(ct);
        return rows.Select(r => MapHouse(r.House, r.Count)).ToList();
    }

    public async Task<PublicHouseDto> GetHouseAsync(string slug, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var house = await db.TranslationHouses.AsNoTracking().FirstOrDefaultAsync(h => h.Slug == normalized && h.Status == HouseStatus.Active, ct)
                    ?? throw Errors.NotFound("Nhóm dịch");
        var count = await VisibleComics.CountAsync(c => c.TranslationHouseId == house.Id, ct);
        return MapHouse(house, count);
    }

    /// <summary>Visible comic cards for the given ids, preserving the given order (invisible ids are skipped).</summary>
    public async Task<IReadOnlyList<ComicCardDto>> CardsByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await ProjectCards(VisibleComics.Where(c => ids.Contains(c.Id))).ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id);
        return ids.Where(byId.ContainsKey).Select(id => MapCard(byId[id])).ToList();
    }

    /// <summary>Comic id of a publicly visible chapter, or null.</summary>
    public Task<Guid?> VisibleChapterComicIdAsync(Guid chapterId, CancellationToken ct) =>
        VisibleChapters.Where(ch => ch.Id == chapterId).Select(ch => (Guid?)ch.ComicId).FirstOrDefaultAsync(ct);

    public Task<bool> IsComicVisibleAsync(Guid comicId, CancellationToken ct) => VisibleComics.AnyAsync(c => c.Id == comicId, ct);

    // ---------------- helpers ----------------
    private sealed record CardRow(
        Guid Id, string Slug, string TranslatedTitle, string OriginalTitle, string? CoverKey, ComicStatus Status, bool IsMature,
        Guid HouseId, string HouseName, string HouseSlug, string? HouseLogoKey, int ChapterCount, DateTimeOffset? LastChapterPublishedAt,
        List<ChapterRow> Latest);

    private sealed record ChapterRow(Guid Id, decimal Number, string? Title, DateTimeOffset? PublishedAt);

    private IQueryable<CardRow> ProjectCards(IQueryable<Comic> query)
    {
        var chapters = VisibleChapters;
        return query.Select(c => new CardRow(
            c.Id, c.Slug, c.TranslatedTitle, c.OriginalTitle, c.CoverKey, c.Status, c.IsMature,
            c.TranslationHouse!.Id, c.TranslationHouse.Name, c.TranslationHouse.Slug, c.TranslationHouse.LogoKey,
            c.PublishedChapterCount, c.LastChapterPublishedAt,
            chapters.Where(ch => ch.ComicId == c.Id).OrderByDescending(ch => ch.ChapterNumber).Take(2)
                .Select(ch => new ChapterRow(ch.Id, ch.ChapterNumber, ch.Title, ch.PublishedAt)).ToList()));
    }

    private ComicCardDto MapCard(CardRow r) => new(
        r.Id, r.Slug, r.TranslatedTitle, r.OriginalTitle, urls.ObjectUrl(r.CoverKey), ApiText.Enum(r.Status), r.IsMature,
        new HouseRef(r.HouseId, r.HouseName, r.HouseSlug, urls.ObjectUrl(r.HouseLogoKey)), r.ChapterCount, r.LastChapterPublishedAt,
        r.Latest.Select(l => new ChapterRef(l.Id, ApiText.ChapterNumber(l.Number), l.Title, l.PublishedAt)).ToList());

    private PublicHouseDto MapHouse(TranslationHouse h, int count) => new(
        h.Id, h.Name, h.Slug, h.Description, urls.ObjectUrl(h.LogoKey), count,
        new HouseLinksDto(h.Links.Facebook, h.Links.Discord, h.Links.Website), h.CreatedAt);

    private async Task<(ChapterSummaryDto Summary, Guid PackageId)> LoadChapterAsync(Guid chapterId, CancellationToken ct)
    {
        var ch = await VisibleChapters.Where(x => x.Id == chapterId)
            .Select(x => new
            {
                x.Id, x.ComicId, x.ChapterNumber, x.Title, x.PublishedAt, x.PageCount, x.PublishedPackageId,
                ComicTitle = x.Comic!.TranslatedTitle, ComicSlug = x.Comic.Slug, x.Comic.IsMature,
                House = new { x.Comic.TranslationHouse!.Id, x.Comic.TranslationHouse.Name, x.Comic.TranslationHouse.Slug, x.Comic.TranslationHouse.LogoKey },
            })
            .FirstOrDefaultAsync(ct);

        if (ch?.PublishedPackageId is null)
        {
            throw Errors.NotFound("Chương");
        }

        var siblings = VisibleChapters.Where(x => x.ComicId == ch.ComicId);
        var prev = await siblings.Where(x => x.ChapterNumber < ch.ChapterNumber).OrderByDescending(x => x.ChapterNumber)
            .Select(x => new { x.Id, x.ChapterNumber, x.Title, x.PublishedAt }).FirstOrDefaultAsync(ct);
        var next = await siblings.Where(x => x.ChapterNumber > ch.ChapterNumber).OrderBy(x => x.ChapterNumber)
            .Select(x => new { x.Id, x.ChapterNumber, x.Title, x.PublishedAt }).FirstOrDefaultAsync(ct);

        var summary = new ChapterSummaryDto(ch.Id, ch.ComicId, ch.ComicTitle, ch.ComicSlug, ch.IsMature, ApiText.ChapterNumber(ch.ChapterNumber), ch.Title,
            ch.PublishedAt, ch.PageCount,
            prev is null ? null : new ChapterRef(prev.Id, ApiText.ChapterNumber(prev.ChapterNumber), prev.Title, prev.PublishedAt),
            next is null ? null : new ChapterRef(next.Id, ApiText.ChapterNumber(next.ChapterNumber), next.Title, next.PublishedAt),
            new HouseRef(ch.House.Id, ch.House.Name, ch.House.Slug, urls.ObjectUrl(ch.House.LogoKey)));
        return (summary, ch.PublishedPackageId.Value);
    }
}
