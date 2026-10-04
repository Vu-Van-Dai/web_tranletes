using System.Diagnostics;
using ComicPlatform.Api.Infrastructure;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Admin;
using ComicPlatform.Application.Settings;
using ComicPlatform.Application.Tenancy;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Processing;
using ComicPlatform.Infrastructure.Storage;

namespace ComicPlatform.Api.Endpoints;

/// <summary>System administration. Metadata only — no page/image data is exposed here.</summary>
public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").WithTags("Admin").RequireSystemAdmin().RequireRateLimiting(RateLimitPolicies.Write);

        // ---- Translation houses ----
        var houses = admin.MapGroup("/translation-houses");
        houses.MapGet("/", async (string? q, string? status, int? page, int? pageSize, AdminHouseService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q, status, page, pageSize, ct)));
        houses.MapPost("/", async (CreateHouseRequest request, AdminHouseService s, CancellationToken ct) =>
        {
            var result = await s.CreateAsync(request, ct);
            return Results.Created($"/api/admin/translation-houses/{result.House.Id}", result);
        });
        houses.MapGet("/{id:guid}", async (Guid id, AdminHouseService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
        houses.MapPut("/{id:guid}", async (Guid id, UpdateHouseRequest request, AdminHouseService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, request, ct)));
        houses.MapPost("/{id:guid}/activate", async (Guid id, AdminHouseService s, CancellationToken ct) => Results.Ok(await s.ActivateAsync(id, ct)));
        houses.MapPost("/{id:guid}/suspend", async (Guid id, SuspendHouseRequest? request, AdminHouseService s, CancellationToken ct) =>
            Results.Ok(await s.SuspendAsync(id, request ?? new SuspendHouseRequest(null), ct)));
        houses.MapGet("/{id:guid}/members", async (Guid id, AdminHouseService s, CancellationToken ct) => Results.Ok(await s.MembersAsync(id, ct)));
        houses.MapPost("/{id:guid}/members", async (Guid id, AddMemberRequest request, AdminHouseService s, CancellationToken ct) =>
            Results.Ok(await s.AddMemberAsync(id, request, ct)));
        houses.MapPut("/{id:guid}/members/{memberId:guid}", async (Guid id, Guid memberId, UpdateMemberRequest request, AdminHouseService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateMemberAsync(id, memberId, request, ct)));
        houses.MapDelete("/{id:guid}/members/{memberId:guid}", async (Guid id, Guid memberId, AdminHouseService s, CancellationToken ct) =>
        {
            await s.RemoveMemberAsync(id, memberId, ct);
            return Results.NoContent();
        });
        houses.MapGet("/{id:guid}/comics", async (Guid id, string? q, int? page, int? pageSize, AdminHouseService s, CancellationToken ct) =>
            Results.Ok(await s.ComicsAsync(id, q, page, pageSize, ct)));

        // ---- Users ----
        var users = admin.MapGroup("/users");
        users.MapGet("/", async (string? q, string? status, int? page, int? pageSize, AdminUserService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(q, status, page, pageSize, ct)));
        users.MapPost("/{id:guid}/disable", async (Guid id, AdminUserService s, CancellationToken ct) =>
        {
            await s.DisableAsync(id, ct);
            return Results.NoContent();
        });
        users.MapPost("/{id:guid}/enable", async (Guid id, AdminUserService s, CancellationToken ct) =>
        {
            await s.EnableAsync(id, ct);
            return Results.NoContent();
        });
        users.MapPost("/{id:guid}/reset-password", async (Guid id, AdminUserService s, CancellationToken ct) =>
            Results.Ok(await s.ResetPasswordAsync(id, ct)));

        // ---- Statistics, audit, settings, system ----
        admin.MapGet("/stats", async (AdminStatsService s, CancellationToken ct) => Results.Ok(await s.GetAsync(ct)));
        admin.MapGet("/audit-logs", async (string? action, Guid? houseId, int? page, int? pageSize, AdminStatsService s, CancellationToken ct) =>
            Results.Ok(await s.AuditAsync(action, houseId, page, pageSize, ct)));
        admin.MapGet("/settings", async (SettingsService s, CancellationToken ct) => Results.Ok(await s.GetAsync(ct)));
        admin.MapPut("/settings", async (UpdateSystemSettingsRequest request, SettingsService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(request, ct)));
        admin.MapGet("/system", (PlatformSettings settings, ProcessingMetrics metrics, IProcessingQueue queue, ImageWorkerPool pool,
            StoragePaths paths, IObjectStore store) =>
        {
            var process = Process.GetCurrentProcess();
            ThreadPool.GetAvailableThreads(out var workerAvailable, out _);
            ThreadPool.GetMaxThreads(out var workerMax, out _);
            long? freeBytes = null;
            try
            {
                freeBytes = new DriveInfo(Path.GetPathRoot(paths.ComicsRoot)!).AvailableFreeSpace;
            }
            catch (IOException)
            {
            }

            return Results.Ok(new
            {
                limits = new
                {
                    settings.Limits.MaxUploadSize,
                    settings.Limits.MaxExtractedSize,
                    settings.Limits.MaxPagesPerChapter,
                    settings.Limits.MaxImagePixels,
                    settings.Limits.MaxImageDimension,
                    settings.Limits.PageMaxWidth,
                    settings.Limits.WebpQuality,
                },
                processing = new
                {
                    imageConcurrency = pool.Concurrency,
                    queueLength = queue.Count,
                    queueCapacity = queue.Capacity,
                    workerBacklog = pool.QueueLength,
                    activeConversions = metrics.ActiveConversions,
                    maxObservedConversions = metrics.MaxObservedConversions,
                    jobsSucceeded = metrics.JobsSucceeded,
                    jobsFailed = metrics.JobsFailed,
                    pagesConverted = metrics.PagesConverted,
                    lastJobMilliseconds = metrics.LastJobMilliseconds,
                    currentJobId = metrics.CurrentJobId,
                },
                runtime = new
                {
                    processorCount = Environment.ProcessorCount,
                    workingSetBytes = process.WorkingSet64,
                    gcHeapBytes = GC.GetTotalMemory(false),
                    totalCpuMilliseconds = (long)process.TotalProcessorTime.TotalMilliseconds,
                    threadPoolBusyWorkers = workerMax - workerAvailable,
                    threadCount = process.Threads.Count,
                    uptimeSeconds = (long)(DateTime.Now - process.StartTime).TotalSeconds,
                },
                storage = new { store = store.Name, freeBytes, publicImageUrl = settings.Storage.PublicImageUrl },
            });
        });

        return api;
    }
}
