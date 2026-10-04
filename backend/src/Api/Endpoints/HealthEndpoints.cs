using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Storage;
using Npgsql;

namespace ComicPlatform.Api.Endpoints;

public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder api)
    {
        var health = api.MapGroup("/health").WithTags("Health").DisableRateLimiting();

        // Liveness: the process is up and serving requests.
        health.MapGet("/", () => Results.Ok(new { status = "ok" }));

        // Readiness: dependencies usable (DB, writable storage with free space, processing queue).
        health.MapGet("/ready", async (NpgsqlDataSource dataSource, StoragePaths paths, StorageSettings storage, IServiceProvider sp, CancellationToken ct) =>
        {
            var checks = new Dictionary<string, object>();
            var healthy = true;

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await using var cmd = dataSource.CreateCommand("SELECT 1");
                await cmd.ExecuteScalarAsync(timeout.Token);
                checks["database"] = "ok";
            }
            catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or TimeoutException)
            {
                checks["database"] = "unavailable";
                healthy = false;
            }

            try
            {
                Directory.CreateDirectory(paths.TempRoot);
                var probe = Path.Combine(paths.TempRoot, $".probe-{Guid.NewGuid():N}");
                await File.WriteAllTextAsync(probe, "ok", ct);
                File.Delete(probe);
                var free = new DriveInfo(Path.GetPathRoot(paths.ComicsRoot)!).AvailableFreeSpace;
                var enough = free >= storage.MinFreeSpaceBytes;
                checks["storage"] = enough ? "ok" : "low_disk_space";
                checks["freeBytes"] = free;
                healthy &= enough;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                checks["storage"] = "not_writable";
                healthy = false;
            }

            if (sp.GetService<IProcessingQueue>() is { } queue)
            {
                checks["queue"] = new { length = queue.Count, capacity = queue.Capacity };
            }

            return Results.Json(new { status = healthy ? "ok" : "unhealthy", checks }, statusCode: healthy ? 200 : 503);
        });

        return api;
    }
}
