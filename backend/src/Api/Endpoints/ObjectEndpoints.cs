using System.Security.Cryptography;
using System.Text;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Persistence;
using ComicPlatform.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace ComicPlatform.Api.Endpoints;

/// <summary>
/// Binary object delivery from the Oracle host:
/// <list type="bullet">
/// <item><c>/internal/objects/{key}</c> — Cloudflare Worker fallback when R2 misses (shared-secret protected,
/// serves only currently public objects from the block volume).</item>
/// <item><c>/cdn/{key}</c> — development/E2E only (PACKAGE_STORE=local): mirrors the Worker's URL scheme
/// and cache headers so the reader works without Cloudflare.</item>
/// </list>
/// </summary>
public static class ObjectEndpoints
{
    private const string ImmutableCache = "public, max-age=31536000, immutable";

    public static void MapObjectEndpoints(this WebApplication app, StorageSettings storage)
    {
        app.MapMethods("/internal/objects/{**key}", ["GET", "HEAD"], async (string key, HttpContext http, AppDbContext db, StoragePaths paths, CancellationToken ct) =>
        {
            if (!SecretMatches(http.Request.Headers["X-Origin-Secret"].ToString(), storage.OriginSharedSecret))
            {
                return Results.NotFound();
            }

            if (!ObjectKeyParser.TryParse(key, out var parsed) || !await IsPublicAsync(db, key, parsed, ct))
            {
                return Results.NotFound();
            }

            var path = paths.AssetPath(key);
            return File.Exists(path) ? Serve(http, path, parsed.ContentType) : Results.NotFound();
        }).ExcludeFromDescription().DisableRateLimiting();

        if (!storage.IsLocalStore)
        {
            return;
        }

        app.MapMethods("/cdn/{**key}", ["GET", "HEAD"], (string key, HttpContext http, LocalObjectStore store) =>
        {
            http.Response.Headers.AccessControlAllowOrigin = "*";
            if (!ObjectKeyParser.TryParse(key, out var parsed) || store.ResolveFile(key) is not { } path)
            {
                http.Response.Headers.CacheControl = "public, max-age=60";
                return Results.NotFound();
            }

            return Serve(http, path, parsed.ContentType);
        }).ExcludeFromDescription().DisableRateLimiting();
    }

    private static PhysicalFileHttpResult Serve(HttpContext http, string path, string contentType)
    {
        var info = new FileInfo(path);
        http.Response.Headers.CacheControl = ImmutableCache;
        var etag = new EntityTagHeaderValue($"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\"");
        return TypedResults.PhysicalFile(path, contentType, enableRangeProcessing: true, lastModified: info.LastWriteTimeUtc, entityTag: etag);
    }

    private static async Task<bool> IsPublicAsync(AppDbContext db, string key, ParsedObjectKey parsed, CancellationToken ct) => parsed.Kind switch
    {
        ObjectKind.Package => await db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant])
            .AnyAsync(p => p.StorageKey == key && p.Status == PackageStatus.Published, ct),
        ObjectKind.Cover => await db.Comics.IgnoreQueryFilters([QueryFilters.Tenant]).AnyAsync(c => c.CoverKey == key, ct),
        ObjectKind.Logo => await db.TranslationHouses.AnyAsync(h => h.LogoKey == key, ct),
        _ => false,
    };

    private static bool SecretMatches(string provided, string expected)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var a = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
