using ComicPlatform.Api.Infrastructure;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Library;
using ComicPlatform.Application.Settings;
using ComicPlatform.Infrastructure.Configuration;

namespace ComicPlatform.Api.Endpoints;

/// <summary>
/// Anonymous catalog API. Responses are identical for every caller (no auth-dependent output), so a
/// short shared cache lifetime is safe and lets Cloudflare absorb release-time bursts.
/// </summary>
public static class PublicEndpoints
{
    public static RouteGroupBuilder MapPublicEndpoints(this RouteGroupBuilder api)
    {
        var pub = api.MapGroup("").WithTags("Public").RequireRateLimiting(RateLimitPolicies.Public);

        pub.MapGet("/site", async (SettingsService s, CancellationToken ct) =>
        {
            var settings = await s.GetAsync(ct);
            return Results.Ok(new { settings.RegistrationEnabled, settings.Announcement });
        }).CachePublic(60);

        pub.MapGet("/comics", async (string? q, string? tag, string? house, string? status, string? sort, int? page, int? pageSize,
                PublicCatalogService s, CancellationToken ct) =>
            Results.Ok(await s.ListComicsAsync(q, tag, house, status, sort, page, pageSize, ct))).CachePublic(30);
        pub.MapGet("/comics/{id:guid}", async (Guid id, PublicCatalogService s, CancellationToken ct) =>
            Results.Ok(await s.GetComicAsync(id, ct))).CachePublic(30);
        pub.MapGet("/comics/{id:guid}/chapters", async (Guid id, PublicCatalogService s, CancellationToken ct) =>
            Results.Ok(await s.ListChaptersAsync(id, ct))).CachePublic(30);
        pub.MapGet("/chapters/{id:guid}", async (Guid id, PublicCatalogService s, CancellationToken ct) =>
            Results.Ok(await s.GetChapterAsync(id, ct))).CachePublic(30);
        pub.MapGet("/chapters/{id:guid}/reader", async (Guid id, PublicCatalogService s, CancellationToken ct) =>
            Results.Ok(await s.GetReaderAsync(id, ct))).CachePublic(30);
        pub.MapGet("/tags", async (PublicCatalogService s, CancellationToken ct) => Results.Ok(await s.ListTagsAsync(ct))).CachePublic(300);
        pub.MapGet("/houses", async (PublicCatalogService s, CancellationToken ct) => Results.Ok(await s.ListHousesAsync(ct))).CachePublic(60);
        pub.MapGet("/houses/{slug}", async (string slug, PublicCatalogService s, CancellationToken ct) =>
            Results.Ok(await s.GetHouseAsync(slug, ct))).CachePublic(60);
        pub.MapGet("/rankings", async (string? period, LibraryService s, CancellationToken ct) =>
            Results.Ok(await s.RankingsAsync(period, ct))).CachePublic(120);

        // View beacon: aggregated in memory, deduplicated per client + chapter, flushed in batches.
        pub.MapPost("/chapters/{id:guid}/view", async (Guid id, HttpContext http, PublicCatalogService s, ViewCounter counter, ICurrentUser user,
            WebSettings web, CancellationToken ct) =>
        {
            var comicId = await s.VisibleChapterComicIdAsync(id, ct);
            if (comicId is null)
            {
                throw Errors.NotFound("Chương");
            }

            counter.Record(comicId.Value, id, user.UserId?.ToString("N") ?? ClientIp.Resolve(http, web));
            return Results.NoContent();
        });

        return api;
    }

    private static RouteHandlerBuilder CachePublic(this RouteHandlerBuilder builder, int seconds) =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers.CacheControl = $"public, max-age={seconds}";
            return await next(ctx);
        });
}

public static class LibraryEndpoints
{
    public static RouteGroupBuilder MapLibraryEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Library").RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Write);

        me.MapGet("/follows", async (LibraryService s, ICurrentUser user, CancellationToken ct) => Results.Ok(await s.FollowsAsync(user.UserId!.Value, ct)));
        me.MapPut("/follows/{comicId:guid}", async (Guid comicId, LibraryService s, ICurrentUser user, CancellationToken ct) =>
        {
            await s.FollowAsync(user.UserId!.Value, comicId, ct);
            return Results.NoContent();
        });
        me.MapDelete("/follows/{comicId:guid}", async (Guid comicId, LibraryService s, ICurrentUser user, CancellationToken ct) =>
        {
            await s.UnfollowAsync(user.UserId!.Value, comicId, ct);
            return Results.NoContent();
        });
        me.MapGet("/history", async (LibraryService s, ICurrentUser user, CancellationToken ct) => Results.Ok(await s.HistoryAsync(user.UserId!.Value, ct)));
        me.MapPut("/progress", async (SaveProgressRequest request, LibraryService s, ICurrentUser user, CancellationToken ct) =>
        {
            await s.SaveProgressAsync(user.UserId!.Value, request, ct);
            return Results.NoContent();
        });
        me.MapGet("/comics/{comicId:guid}/state", async (Guid comicId, LibraryService s, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await s.StateAsync(user.UserId!.Value, comicId, ct)));

        return api;
    }
}
