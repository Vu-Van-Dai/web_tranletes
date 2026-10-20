using System.Text;
using System.Xml;
using ComicPlatform.Api.Infrastructure;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Api.Endpoints;

/// <summary>Search-engine sitemap for public comic pages.</summary>
public static class SitemapEndpoints
{
    private const string SiteOrigin = "https://rongnho.shop";

    public static IEndpointRouteBuilder MapSitemapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sitemap.xml", async (IAppDbContext db, CancellationToken ct) =>
        {
            // Sitemap must only contain comics available to anonymous visitors.
            var comics = await db.Comics
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .AsNoTracking()
                .Where(c => c.DeletedAt == null
                    && c.PublishedChapterCount > 0
                    && c.TranslationHouse != null
                    && c.TranslationHouse.Status == HouseStatus.Active)
                .OrderByDescending(c => c.LastChapterPublishedAt)
                .Select(c => new { c.Id, c.Slug, c.LastChapterPublishedAt, c.UpdatedAt })
                .Take(49997)
                .ToListAsync(ct);

            using var stream = new MemoryStream();
            var settings = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false), Indent = true };
            using (var writer = XmlWriter.Create(stream, settings))
            {
                await writer.WriteStartDocumentAsync();
                await writer.WriteStartElementAsync(null, "urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

                async Task WriteUrl(string loc, DateTimeOffset? modified)
                {
                    await writer.WriteStartElementAsync(null, "url", null);
                    await writer.WriteElementStringAsync(null, "loc", null, loc);
                    if (modified is not null)
                        await writer.WriteElementStringAsync(null, "lastmod", null, modified.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                    await writer.WriteEndElementAsync();
                }

                await WriteUrl($"{SiteOrigin}/", DateTimeOffset.UtcNow);
                await WriteUrl($"{SiteOrigin}/truyen", DateTimeOffset.UtcNow);
                await WriteUrl($"{SiteOrigin}/nhom-dich", DateTimeOffset.UtcNow);

                foreach (var comic in comics)
                {
                    // This matches the frontend route: /truyen/{id}/{slug}
                    var slug = Uri.EscapeDataString(comic.Slug);
                    await WriteUrl($"{SiteOrigin}/truyen/{comic.Id:D}/{slug}", comic.LastChapterPublishedAt ?? comic.UpdatedAt);
                }

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
