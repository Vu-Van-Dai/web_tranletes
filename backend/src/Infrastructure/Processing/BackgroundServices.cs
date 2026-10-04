using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Library;
using ComicPlatform.Application.Publishing;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Publishing;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Persistence;
using ComicPlatform.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace ComicPlatform.Infrastructure.Processing;

/// <summary>
/// Runs chapter jobs one at a time (image-level parallelism comes from the worker pool).
/// On start it recovers jobs interrupted by a crash/restart; a periodic sweep re-enqueues
/// queued jobs that did not fit in the in-memory channel.
/// </summary>
public sealed class ProcessingHostedService(
    ProcessingQueue queue,
    ChapterProcessingPipeline pipeline,
    IServiceScopeFactory scopes,
    IUploadStore uploads,
    ProcessingSettings settings,
    TimeProvider clock,
    ILogger<ProcessingHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            await RecoverAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Job recovery failed");
        }

        using var sweepTimer = new PeriodicTimer(SweepInterval);
        var sweep = SweepLoopAsync(sweepTimer, stoppingToken);

        try
        {
            await foreach (var jobId in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await pipeline.RunAsync(jobId, settings.JobTimeout, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Unhandled error while processing job {JobId}", jobId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await sweep;
    }

    private async Task SweepLoopAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await EnqueueQueuedAsync(olderThan: TimeSpan.FromSeconds(20), ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Processing sweep failed");
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var interrupted = await db.ProcessingJobs.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(j => j.Status == JobStatus.Running)
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        foreach (var job in interrupted)
        {
            if (job.Attempts < ProcessingJob.MaxAttempts && uploads.Exists(job.UploadPath))
            {
                job.Requeue(now);
                logger.LogWarning("Re-queued interrupted job {JobId} (attempt {Attempt})", job.Id, job.Attempts);
            }
            else
            {
                job.Fail("interrupted", "Quá trình xử lý bị gián đoạn. Vui lòng bấm \"Thử lại\" hoặc tải chương lên lại.", now);
                var package = await db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(p => p.Id == job.PackageId, ct);
                package?.MarkFailed();
                var chapter = await db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(c => c.Id == job.ChapterId, ct);
                chapter?.FailProcessing(now);
                logger.LogWarning("Marked interrupted job {JobId} as failed", job.Id);
            }
        }

        await db.SaveChangesAsync(ct);
        await EnqueueQueuedAsync(TimeSpan.Zero, ct);
    }

    private async Task EnqueueQueuedAsync(TimeSpan olderThan, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = clock.GetUtcNow() - olderThan;
        var ids = await db.ProcessingJobs.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(j => j.Status == JobStatus.Queued && j.CreatedAt <= cutoff)
            .OrderBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .Take(queue.Capacity)
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            if (!queue.TryEnqueue(id))
            {
                break;
            }
        }
    }
}

/// <summary>Executes the storage outbox: object deletions and edge-cache purges, with retries.</summary>
public sealed class StorageTaskProcessor(
    IServiceScopeFactory scopes,
    IObjectStore store,
    ICdnPurger purger,
    IDistributedLock locks,
    StoragePaths paths,
    ProcessingSettings settings,
    TimeProvider clock,
    ILogger<StorageTaskProcessor> logger) : BackgroundService
{
    private const int MaxAttempts = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.OutboxInterval);
        do
        {
            try
            {
                while (await RunOnceAsync(stoppingToken) > 0 && !stoppingToken.IsCancellationRequested)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Storage outbox run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Processes one batch; returns how many tasks completed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var tasks = await db.StorageTasks.Where(t => t.NextAttemptAt <= now).OrderBy(t => t.CreatedAt).Take(25).ToListAsync(ct);
        var completed = 0;
        foreach (var task in tasks)
        {
            try
            {
                if (task.Kind == StorageTaskKind.PurgeUrl)
                {
                    await purger.PurgeAsync([task.Key], ct);
                }
                else if (task.PackageId is { } packageId)
                {
                    await using (await locks.AcquireAsync(PublishingService.LockKey(packageId), ct))
                    {
                        var status = await db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant])
                            .Where(p => p.Id == packageId).Select(p => (PackageStatus?)p.Status).FirstOrDefaultAsync(ct);
                        if (status != PackageStatus.Published)
                        {
                            await store.DeleteAsync(task.Key, ct);
                            logger.LogInformation("Withdrew object {Key} from {Store}", task.Key, store.Name);
                        }
                    }
                }
                else if (!await IsReferencedAsync(db, task.Key, ct))
                {
                    await store.DeleteAsync(task.Key, ct);
                    DeleteLocalAsset(task.Key);
                    logger.LogInformation("Deleted unreferenced object {Key} from {Store}", task.Key, store.Name);
                }

                db.StorageTasks.Remove(task);
                completed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                task.RecordFailure(ex.Message, clock.GetUtcNow());
                if (task.Attempts >= MaxAttempts)
                {
                    logger.LogError("Giving up storage task {TaskId} ({Kind} {Key}) after {Attempts} attempts", task.Id, task.Kind, task.Key, task.Attempts);
                    db.StorageTasks.Remove(task);
                }
                else
                {
                    logger.LogWarning("Storage task {TaskId} failed (attempt {Attempts}): {Error}", task.Id, task.Attempts, ex.Message);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return completed;
    }

    private static async Task<bool> IsReferencedAsync(AppDbContext db, string key, CancellationToken ct)
    {
        if (!ObjectKeyParser.TryParse(key, out var parsed))
        {
            return false;
        }

        return parsed.Kind switch
        {
            ObjectKind.Cover => await db.Comics.IgnoreQueryFilters([QueryFilters.Tenant]).AnyAsync(c => c.CoverKey == key, ct),
            ObjectKind.Logo => await db.TranslationHouses.AnyAsync(h => h.LogoKey == key, ct),
            _ => await db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant]).AnyAsync(p => p.StorageKey == key && p.Status == PackageStatus.Published, ct),
        };
    }

    private void DeleteLocalAsset(string key)
    {
        if (!ObjectKeyParser.TryParse(key, out var parsed) || parsed.Kind == ObjectKind.Package)
        {
            return;
        }

        try
        {
            File.Delete(paths.AssetPath(key));
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Hourly cleanup: stale temp files, expired retry uploads, purge of long-deleted comics.</summary>
public sealed class JanitorService(
    IServiceScopeFactory scopes,
    StoragePaths paths,
    ProcessingSettings settings,
    ProcessingMetrics metrics,
    TimeProvider clock,
    ILogger<JanitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.JanitorInterval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Janitor run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();

        // 1) Uploads: keep files of active jobs and of recently failed jobs (retry window).
        var retryCutoff = now - settings.UploadRetention;
        var keep = (await db.ProcessingJobs.IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running || (j.Status == JobStatus.Failed && j.FinishedAt > retryCutoff))
                .Select(j => j.UploadPath)
                .ToListAsync(ct))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uploadsDir = Path.Combine(paths.TempRoot, "uploads");
        var removed = 0;
        if (Directory.Exists(uploadsDir))
        {
            foreach (var file in Directory.EnumerateFiles(uploadsDir))
            {
                var age = now - File.GetLastWriteTimeUtc(file);
                if (!keep.Contains(Path.GetFullPath(file)) && age > TimeSpan.FromHours(1))
                {
                    TryDelete(() => File.Delete(file));
                    removed++;
                }
            }
        }

        // 2) Work directories not belonging to the running job.
        var workDir = Path.Combine(paths.TempRoot, "work");
        if (Directory.Exists(workDir))
        {
            var current = metrics.CurrentJobId?.ToString("D");
            foreach (var dir in Directory.EnumerateDirectories(workDir))
            {
                if (Path.GetFileName(dir) != current && now - Directory.GetLastWriteTimeUtc(dir) > TimeSpan.FromHours(6))
                {
                    TryDelete(() => Directory.Delete(dir, recursive: true));
                    removed++;
                }
            }
        }

        // 3) Hard-delete comics soft-deleted longer than the retention period.
        var deleteCutoff = now - settings.DeletedComicRetention;
        var expired = await db.Comics.IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null && c.DeletedAt < deleteCutoff)
            .OrderBy(c => c.DeletedAt)
            .Select(c => c.Id)
            .Take(20)
            .ToListAsync(ct);
        var outbox = scope.ServiceProvider.GetRequiredService<StorageOutbox>();
        foreach (var comicId in expired)
        {
            var keys = await db.ChapterPackages.IgnoreQueryFilters().Where(p => p.Chapter!.ComicId == comicId).Select(p => p.StorageKey).ToListAsync(ct);
            foreach (var key in keys)
            {
                outbox.Withdraw(key, null);
            }

            await db.SaveChangesAsync(ct);
            await db.Comics.IgnoreQueryFilters().Where(c => c.Id == comicId).ExecuteDeleteAsync(ct);
            TryDelete(() =>
            {
                var dir = paths.ComicDir(comicId);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            });
            logger.LogInformation("Purged comic {ComicId} deleted before {Cutoff}", comicId, deleteCutoff);
        }

        if (removed > 0)
        {
            logger.LogInformation("Janitor removed {Count} stale temp entries", removed);
        }
    }

    private static void TryDelete(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Flushes aggregated view counts every minute with a single batched upsert.</summary>
public sealed class ViewFlushService(ViewCounter counter, NpgsqlDataSource dataSource, ILogger<ViewFlushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }

        await FlushAsync(CancellationToken.None);
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        // One snapshot so ids/days/views stay aligned even if a late Record() touches the drained map.
        var drained = counter.Drain().ToArray();
        if (drained.Length == 0)
        {
            return;
        }

        try
        {
            await using var cmd = dataSource.CreateCommand("""
                INSERT INTO comic_view_daily (comic_id, day, views)
                SELECT u.comic_id, u.day, u.views
                FROM unnest(@ids, @days, @views) AS u(comic_id, day, views)
                WHERE EXISTS (SELECT 1 FROM comics c WHERE c.id = u.comic_id)
                ON CONFLICT (comic_id, day) DO UPDATE SET views = comic_view_daily.views + EXCLUDED.views
                """);
            cmd.Parameters.Add(new NpgsqlParameter<Guid[]>("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { TypedValue = drained.Select(k => k.Key.ComicId).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter<DateOnly[]>("days", NpgsqlDbType.Array | NpgsqlDbType.Date) { TypedValue = drained.Select(k => k.Key.Day).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter<long[]>("views", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { TypedValue = drained.Select(k => k.Value).ToArray() });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not flush {Count} view counters", drained.Length);
        }
    }
}
