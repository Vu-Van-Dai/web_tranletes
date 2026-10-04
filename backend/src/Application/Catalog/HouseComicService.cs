using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Publishing;
using ComicPlatform.Application.Tenancy;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Application.Catalog;

/// <summary>Comic management for the caller's house. Every query runs through the tenant filter.</summary>
public sealed class HouseComicService(
    IAppDbContext db,
    ITenantContext tenant,
    TenantResourceGuard guard,
    TagService tags,
    IMediaService media,
    IPublicUrls urls,
    StorageOutbox outbox,
    IAuditLogger audit,
    TimeProvider clock,
    ILogger<HouseComicService> logger)
{
    public async Task<PagedResult<HouseComicListItem>> ListAsync(string? q, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        var paging = PageRequest.Normalize(page, pageSize, 20, 100);
        var query = db.Comics.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var key = TextNormalizer.ToSearchKey(q);
            query = query.Where(c => EF.Functions.ILike(c.SearchKey, "%" + EscapeLike(key) + "%", "\\"));
        }

        if (ApiText.TryParseEnum<ComicStatus>(status, out var st))
        {
            query = query.Where(c => c.Status == st);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(paging.Skip).Take(paging.PageSize)
            .Select(c => new
            {
                c.Id, c.Slug, c.TranslatedTitle, c.OriginalTitle, c.CoverKey, c.Status, c.IsMature, c.PublishedChapterCount, c.UpdatedAt,
                ChapterCount = db.Chapters.Count(ch => ch.ComicId == c.Id),
            })
            .ToListAsync(ct);

        return new PagedResult<HouseComicListItem>(
            rows.Select(r => new HouseComicListItem(r.Id, r.Slug, r.TranslatedTitle, r.OriginalTitle, urls.ObjectUrl(r.CoverKey),
                ApiText.Enum(r.Status), r.IsMature, r.ChapterCount, r.PublishedChapterCount, r.UpdatedAt)).ToList(),
            paging.Page, paging.PageSize, total);
    }

    public async Task<HouseComicDetail> CreateAsync(SaveComicRequest request, CancellationToken ct)
    {
        var (details, requestedSlug) = Validate(request);
        var slug = await UniqueSlugAsync(requestedSlug ?? TextNormalizer.ToSlug(details.TranslatedTitle, 100, "truyen"), null, requestedSlug is not null, ct);
        var comic = new Comic(tenant.HouseId, details, slug, clock.GetUtcNow());
        comic.SetTags(await tags.EnsureTagsAsync(request.Tags, ct));
        db.Comics.Add(comic);
        audit.Add(AuditActions.ComicCreated, "comic", comic.Id, tenant.HouseId, new { comic.TranslatedTitle });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Comic {ComicId} created in house {HouseId}", comic.Id, tenant.HouseId);
        return await GetAsync(comic.Id, ct);
    }

    public async Task<HouseComicDetail> GetAsync(Guid comicId, CancellationToken ct)
    {
        var comic = await guard.GetComicAsync(comicId, ct, includeTags: true);
        var chapterCount = await db.Chapters.CountAsync(ch => ch.ComicId == comicId, ct);
        return Map(comic, chapterCount);
    }

    public async Task<HouseComicDetail> UpdateAsync(Guid comicId, SaveComicRequest request, CancellationToken ct)
    {
        var comic = await guard.GetComicAsync(comicId, ct, includeTags: true);
        var (details, requestedSlug) = Validate(request);
        var slug = requestedSlug is null || requestedSlug == comic.Slug
            ? comic.Slug
            : await UniqueSlugAsync(requestedSlug, comic.Id, true, ct);
        comic.Update(details, slug, clock.GetUtcNow());
        comic.SetTags(await tags.EnsureTagsAsync(request.Tags, ct));
        await db.SaveChangesAsync(ct);
        return await GetAsync(comicId, ct);
    }

    public async Task DeleteAsync(Guid comicId, CancellationToken ct)
    {
        var comic = await guard.GetComicAsync(comicId, ct);
        var now = clock.GetUtcNow();

        if (await db.ProcessingJobs.AnyAsync(j => j.Chapter!.ComicId == comicId && (j.Status == Domain.Publishing.JobStatus.Queued || j.Status == Domain.Publishing.JobStatus.Running), ct))
        {
            throw Errors.Conflict("comic_processing", "Truyện đang có chương được xử lý, vui lòng đợi hoàn tất rồi xoá.");
        }

        // Take everything public offline immediately; files are purged later by the janitor.
        var published = await db.Chapters.Where(ch => ch.ComicId == comicId && ch.Status == ChapterStatus.Published).ToListAsync(ct);
        var packageIds = published.Where(ch => ch.PublishedPackageId != null).Select(ch => ch.PublishedPackageId!.Value).ToList();
        var packages = await db.ChapterPackages.Where(p => packageIds.Contains(p.Id)).ToListAsync(ct);
        foreach (var chapter in published)
        {
            chapter.Unpublish(now);
        }

        foreach (var package in packages)
        {
            package.MarkUnpublished();
            outbox.Withdraw(package.StorageKey, package.Id);
        }

        if (comic.CoverKey is not null)
        {
            outbox.Withdraw(comic.CoverKey, null);
        }

        comic.MarkDeleted(now);
        audit.Add(AuditActions.ComicDeleted, "comic", comic.Id, tenant.HouseId, new { comic.TranslatedTitle, unpublishedChapters = published.Count });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Comic {ComicId} soft-deleted in house {HouseId}", comicId, tenant.HouseId);
    }

    public async Task<HouseComicDetail> SetCoverAsync(Guid comicId, Stream image, CancellationToken ct)
    {
        var comic = await guard.GetComicAsync(comicId, ct);
        var stored = await media.StoreCoverAsync(comicId, image, ct);
        var old = comic.CoverKey;
        comic.SetCover(stored.Key, clock.GetUtcNow());
        if (old is not null && old != stored.Key)
        {
            outbox.Withdraw(old, null);
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(comicId, ct);
    }

    private (ComicDetails Details, string? Slug) Validate(SaveComicRequest r)
    {
        var v = new Validator();
        v.Required("translatedTitle", r.TranslatedTitle, "Tên truyện (tiếng Việt)").Length("translatedTitle", r.TranslatedTitle, "Tên truyện (tiếng Việt)", 1, 300);
        v.Required("originalTitle", r.OriginalTitle, "Tên gốc").Length("originalTitle", r.OriginalTitle, "Tên gốc", 1, 300);
        v.MaxLength("description", r.Description, "Giới thiệu", 5000);
        v.MaxLength("author", r.Author, "Tác giả", 200);
        v.MaxLength("artist", r.Artist, "Hoạ sĩ", 200);
        var slug = string.IsNullOrWhiteSpace(r.Slug) ? null : r.Slug.Trim().ToLowerInvariant();
        v.Slug("slug", slug).MaxLength("slug", slug, "Đường dẫn", 100);
        var status = ComicStatus.Ongoing;
        if (r.Status is not null && !ApiText.TryParseEnum(r.Status, out status))
        {
            v.Add("status", "Trạng thái truyện không hợp lệ.");
        }

        v.ThrowIfInvalid();
        return (new ComicDetails(r.OriginalTitle!, r.TranslatedTitle!, string.IsNullOrWhiteSpace(r.Description) ? null : r.Description, r.Author, r.Artist, status, r.IsMature ?? false), slug);
    }

    private async Task<string> UniqueSlugAsync(string baseSlug, Guid? excludeId, bool explicitSlug, CancellationToken ct)
    {
        var taken = await db.Comics
            .Where(c => c.Id != excludeId && (c.Slug == baseSlug || c.Slug.StartsWith(baseSlug + "-")))
            .Select(c => c.Slug)
            .ToListAsync(ct);

        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        if (explicitSlug)
        {
            throw Errors.Field("slug", "Đường dẫn này đã được dùng cho truyện khác trong nhóm.");
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private HouseComicDetail Map(Comic c, int chapterCount) => new(
        c.Id, c.Slug, c.TranslatedTitle, c.OriginalTitle, c.Description, c.Author, c.Artist, urls.ObjectUrl(c.CoverKey),
        ApiText.Enum(c.Status), c.IsMature,
        c.Tags.Where(t => t.Tag is not null).Select(t => new TagDto(t.Tag!.Id, t.Tag.Name, t.Tag.Slug)).OrderBy(t => t.Name).ToList(),
        chapterCount, c.PublishedChapterCount, c.CreatedAt, c.UpdatedAt);

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
