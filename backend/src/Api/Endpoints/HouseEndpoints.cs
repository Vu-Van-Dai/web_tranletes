using ComicPlatform.Api.Infrastructure;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Analytics;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Application.Publishing;
using ComicPlatform.Application.Tenancy;
using ComicPlatform.Domain.Publishing;
using ComicPlatform.Domain.Tenancy;
using ComicPlatform.Infrastructure.Configuration;

namespace ComicPlatform.Api.Endpoints;

/// <summary>
/// Translation-house workspace. The whole group resolves the house context from DB memberships first
/// (fail closed); each endpoint then declares the permission it needs. House ids are never read from bodies.
/// </summary>
public static class HouseEndpoints
{
    public static RouteGroupBuilder MapHouseEndpoints(this RouteGroupBuilder api)
    {
        var house = api.MapGroup("/translation-house").WithTags("TranslationHouse").RequireHouseContext().RequireRateLimiting(RateLimitPolicies.Write);

        // ---- Profile ----
        house.MapGet("/profile", async (HouseProfileService s, CancellationToken ct) => Results.Ok(await s.GetAsync(ct)));
        house.MapPut("/profile", async (UpdateHouseProfileRequest request, HouseProfileService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(request, ct))).RequirePermission(HousePermissions.ManageProfile);
        house.MapPut("/profile/logo", async (HttpContext http, HouseProfileService s, LimitSettings limits, CancellationToken ct) =>
            Results.Ok(await MultipartUpload.ReadFirstFileAsync(http, limits.MaxCoverSize, (stream, _) => s.SetLogoAsync(stream, ct), ct)))
            .RequirePermission(HousePermissions.ManageProfile);
        house.MapPut("/profile/cover", async (HttpContext http, HouseProfileService s, LimitSettings limits, CancellationToken ct) =>
            Results.Ok(await MultipartUpload.ReadFirstFileAsync(http, limits.MaxCoverSize, (stream, _) => s.SetCoverAsync(stream, ct), ct)))
            .RequirePermission(HousePermissions.ManageProfile);
        house.MapDelete("/profile/cover", async (HouseProfileService s, CancellationToken ct) => Results.Ok(await s.RemoveCoverAsync(ct)))
            .RequirePermission(HousePermissions.ManageProfile);
        house.MapGet("/dashboard", async (HouseChapterService s, CancellationToken ct) => Results.Ok(await s.DashboardAsync(ct)));
        // Lets the browser check an archive against the same limits before uploading it.
        house.MapGet("/upload-limits", (LimitSettings l) => Results.Ok(new
        {
            l.MaxUploadSize,
            l.MaxExtractedSize,
            MaxPages = l.MaxPagesPerChapter,
            l.MaxEntrySize,
            l.MaxCompressionRatio,
            l.MaxImageDimension,
            l.MaxImagePixels,
            l.PageMaxWidth,
        }));

        // ---- Members ----
        house.MapGet("/members", async (MemberManager m, ITenantContext tenant, CancellationToken ct) =>
            Results.Ok(await m.ListAsync(tenant.HouseId, includeEmail: tenant.Permissions.HasFlag(HousePermissions.ManageMembers), ct)))
            .RequirePermission(HousePermissions.ViewMembers);
        house.MapPost("/members", async (AddMemberRequest request, MemberManager m, ITenantContext tenant, CancellationToken ct) =>
            Results.Ok(await m.AddAsync(tenant.HouseId, request, ct))).RequirePermission(HousePermissions.ManageMembers);
        house.MapPut("/members/{id:guid}", async (Guid id, UpdateMemberRequest request, MemberManager m, ITenantContext tenant, CancellationToken ct) =>
            Results.Ok(await m.UpdateAsync(tenant.HouseId, id, request, ct))).RequirePermission(HousePermissions.ManageMembers);
        house.MapDelete("/members/{id:guid}", async (Guid id, MemberManager m, ITenantContext tenant, CancellationToken ct) =>
        {
            await m.RemoveAsync(tenant.HouseId, id, ct);
            return Results.NoContent();
        }).RequirePermission(HousePermissions.ManageMembers);

        // ---- Comics ----
        house.MapGet("/comics", async (string? q, string? status, int? page, int? pageSize, HouseComicService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q, status, page, pageSize, ct)));
        house.MapPost("/comics", async (SaveComicRequest request, HouseComicService s, CancellationToken ct) =>
        {
            var comic = await s.CreateAsync(request, ct);
            return Results.Created($"/api/translation-house/comics/{comic.Id}", comic);
        }).RequirePermission(HousePermissions.ManageComics);
        house.MapGet("/comics/{id:guid}", async (Guid id, HouseComicService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
        house.MapPut("/comics/{id:guid}", async (Guid id, SaveComicRequest request, HouseComicService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, request, ct))).RequirePermission(HousePermissions.ManageComics);
        house.MapDelete("/comics/{id:guid}", async (Guid id, HouseComicService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(HousePermissions.ManageComics);
        house.MapPut("/comics/{id:guid}/cover", async (Guid id, HttpContext http, HouseComicService s, LimitSettings limits, CancellationToken ct) =>
            Results.Ok(await MultipartUpload.ReadFirstFileAsync(http, limits.MaxCoverSize, (stream, _) => s.SetCoverAsync(id, stream, ct), ct)))
            .RequirePermission(HousePermissions.ManageComics);

        // ---- Chapters ----
        house.MapGet("/comics/{id:guid}/chapters", async (Guid id, HouseChapterService s, CancellationToken ct) => Results.Ok(await s.ListAsync(id, ct)));
        house.MapPost("/comics/{id:guid}/chapters", async (Guid id, SaveChapterRequest request, HouseChapterService s, CancellationToken ct) =>
        {
            var chapter = await s.CreateAsync(id, request, ct);
            return Results.Created($"/api/translation-house/chapters/{chapter.Id}", chapter);
        }).RequirePermission(HousePermissions.ManageChapters);
        house.MapGet("/chapters/{id:guid}", async (Guid id, HouseChapterService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
        house.MapPut("/chapters/{id:guid}", async (Guid id, SaveChapterRequest request, HouseChapterService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, request, ct))).RequirePermission(HousePermissions.ManageChapters);
        house.MapDelete("/chapters/{id:guid}", async (Guid id, HouseChapterService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(HousePermissions.ManageChapters);
        house.MapGet("/chapters/{id:guid}/processing", async (Guid id, HouseChapterService s, CancellationToken ct) =>
            Results.Ok(await s.GetLatestJobAsync(id, ct)));

        // Optional promotional images ("promoStart"/"promoEnd", at most 2) precede the archive part ("file").
        house.MapPost("/chapters/{id:guid}/upload", async (Guid id, HttpContext http, PublishingService s, PublishingOptions options, LimitSettings limits, CancellationToken ct) =>
        {
            s.EnsureQueueAccepting();
            var uploadId = Guid.CreateVersion7();
            var extras = new List<ExtraPageFile>();
            var handedOver = false;
            try
            {
                var job = await MultipartUpload.ReadChapterUploadAsync(http, options.MaxUploadSize, limits.MaxCoverSize, ExtraPageFile.MaxPerUpload, PublishingService.ExtraPageFields,
                    async (field, stream, fileName, index) => extras.Add(await s.SaveExtraPageAsync(uploadId, index, field, stream, fileName, limits.MaxCoverSize, ct)),
                    (stream, fileName) =>
                    {
                        handedOver = true; // AcceptUploadAsync owns the extra files from here on.
                        return s.AcceptUploadAsync(id, stream, fileName, extras, ct);
                    }, ct);
                return Results.Accepted($"/api/translation-house/chapters/{id}/processing", job);
            }
            catch when (!handedOver)
            {
                s.DiscardExtraPages(extras);
                throw;
            }
        })
        .RequirePermission(HousePermissions.UploadChapters)
        .RequireRateLimiting(RateLimitPolicies.Upload)
        .WithRequestTimeout("upload");

        // Pre-publish preview: house members only, never cached, served from the origin's own storage.
        house.MapGet("/chapters/{id:guid}/versions/{version:int}/preview", async (Guid id, int version, HouseChapterService s, CancellationToken ct) =>
            Results.Ok(await s.PreviewAsync(id, version, ct)));
        house.MapGet("/chapters/{id:guid}/versions/{version:int}/package", async (Guid id, int version, HttpContext http, HouseChapterService s, CancellationToken ct) =>
        {
            var path = await s.PreviewPackagePathAsync(id, version, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(path, "application/octet-stream");
        });
        house.MapPost("/chapters/{id:guid}/publish", async (Guid id, PublishingService s, CancellationToken ct) => Results.Ok(await s.PublishAsync(id, ct)))
            .RequirePermission(HousePermissions.PublishChapters)
            .WithRequestTimeout("publish");
        house.MapPost("/chapters/{id:guid}/unpublish", async (Guid id, PublishingService s, CancellationToken ct) => Results.Ok(await s.UnpublishAsync(id, ct)))
            .RequirePermission(HousePermissions.PublishChapters);
        house.MapPost("/chapters/{id:guid}/retry", async (Guid id, PublishingService s, CancellationToken ct) => Results.Ok(await s.RetryAsync(id, ct)))
            .RequirePermission(HousePermissions.UploadChapters);

        // Verified views of the house's chapters (series = sum of chapters); revenue is shown to owners only.
        house.MapGet("/stats/views", async (DateOnly? from, DateOnly? to, ViewReportService s, CancellationToken ct) =>
            Results.Ok(await s.HouseReportAsync(from, to, ct)));

        house.MapGet("/processing", async (int? take, HouseChapterService s, CancellationToken ct) => Results.Ok(await s.ListRecentJobsAsync(take ?? 50, ct)));

        return api;
    }
}
