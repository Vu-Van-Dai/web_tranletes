using System.Globalization;
using System.Text;
using System.Xml;
using ComicPlatform.Api.Infrastructure;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Api.Endpoints;

public static class SitemapEndpoints
{
    private const string SiteOrigin = "https://rongnho.shop";
    private const int MaxUrls = 50_000;
    private static string ComicPath(Guid id, string house, string slug, bool friendly) =>
        friendly ? $"/truyen/{Uri.EscapeDataString(house)}/{Uri.EscapeDataString(slug)}" : $"/truyen/{id:D}/{Uri.EscapeDataString(slug)}";
    private static string ChapterPath(Guid id, string house, string slug, decimal number, bool friendly) =>
        friendly ? $"/doc/{Uri.EscapeDataString(house)}/{Uri.EscapeDataString(slug)}/chuong-{number.ToString("0.##", CultureInfo.InvariantCulture)}" : $"/doc/{id:D}";

    public static IEndpointRouteBuilder MapSitemapEndpoints(this IEndpointRouteBuilder app)
    {
        // Opt in to friendly URLs only after the matching frontend routes are deployed.
        app.MapGet("/sitemap.xml", async (bool? friendly, IAppDbContext db, CancellationToken ct) =>
        {
            var visible = db.Comics.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
                .Where(c => c.DeletedAt == null && c.PublishedChapterCount > 0 &&
                    c.TranslationHouse != null && c.TranslationHouse.Status == HouseStatus.Active);
            var comics = await visible.OrderByDescending(c => c.LastChapterPublishedAt).ThenBy(c => c.Id)
                .Select(c => new { c.Id, c.Slug, House = c.TranslationHouse!.Slug, c.LastChapterPublishedAt, c.UpdatedAt })
                .Take(MaxUrls - 8).ToListAsync(ct);
            var remaining = MaxUrls - 8 - comics.Count;
            var houses = await db.TranslationHouses.AsNoTracking()
                .Where(h => h.Status == HouseStatus.Active && visible.Any(c => c.TranslationHouseId == h.Id))
                .OrderBy(h => h.Slug).Select(h => h.Slug).Take(remaining).ToListAsync(ct);
            remaining -= houses.Count;
            var tags = await db.Tags.AsNoTracking()
                .Where(t => visible.Any(c => c.Tags.Any(ctag => ctag.TagId == t.Id)))
                .OrderBy(t => t.Slug).Select(t => t.Slug).Take(remaining).ToListAsync(ct);
            remaining -= tags.Count;
            var chapters = await db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
                .Where(ch => ch.Status == ChapterStatus.Published && ch.PublishedPackageId != null &&
                    visible.Any(c => c.Id == ch.ComicId && !c.IsMature))
                .OrderByDescending(ch => ch.PublishedAt).ThenBy(ch => ch.Id)
                .Select(ch => new { ch.Id, ch.ChapterNumber, ch.PublishedAt, ch.UpdatedAt,
                    Slug = ch.Comic!.Slug, House = ch.Comic.TranslationHouse!.Slug })
                .Take(remaining).ToListAsync(ct);

            using var stream = new MemoryStream();
            var settings = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false), Indent = true };
            using (var writer = XmlWriter.Create(stream, settings))
            {
                await writer.WriteStartDocumentAsync();
                await writer.WriteStartElementAsync(null, "urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

                async Task WriteUrl(string path, DateTimeOffset? modified = null)
                {
                    await writer.WriteStartElementAsync(null, "url", null);
                    await writer.WriteElementStringAsync(null, "loc", null, SiteOrigin + path);
                    if (modified is not null)
                        await writer.WriteElementStringAsync(null, "lastmod", null, modified.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                    await writer.WriteEndElementAsync();
                }

                foreach (var path in new[] { "/", "/truyen", "/nhom-dich", "/gioi-thieu", "/chinh-sach-bao-mat", "/lien-he", "/dieu-khoan-su-dung", "/faq" })
                    await WriteUrl(path);
                foreach (var comic in comics)
                    await WriteUrl(ComicPath(comic.Id, comic.House, comic.Slug, friendly == true),
                        comic.LastChapterPublishedAt is { } published && published > comic.UpdatedAt ? published : comic.UpdatedAt);
                foreach (var house in houses)
                    await WriteUrl("/nhom-dich/" + Uri.EscapeDataString(house));
                foreach (var tag in tags)
                    await WriteUrl("/the-loai/" + Uri.EscapeDataString(tag));
                foreach (var chapter in chapters)
                    await WriteUrl(ChapterPath(chapter.Id, chapter.House, chapter.Slug, chapter.ChapterNumber, friendly == true),
                        chapter.PublishedAt is { } published && published > chapter.UpdatedAt ? published : chapter.UpdatedAt);

                await writer.WriteEndElementAsync();
                await writer.WriteEndDocumentAsync();
                await writer.FlushAsync();
            }

            return Results.Bytes(stream.ToArray(), "application/xml; charset=utf-8");
        })
        .AllowAnonymous()
        .RequireRateLimiting(RateLimitPolicies.Public)
        .WithName("SitemapXml");

        app.MapGet("/robots.txt", () => Results.Text(
            "User-agent: *\nAllow: /\nDisallow: /quan-tri/\nDisallow: /quan-ly-nhom/\nDisallow: /dang-nhap\nDisallow: /dang-ky\nDisallow: /tu-truyen\nSitemap: https://rongnho.shop/sitemap.xml\n",
            "text/plain; charset=utf-8"))
            .AllowAnonymous();

        return app;
    }
}
