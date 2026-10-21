using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Settings;
using ComicPlatform.Application.Analytics;
using ComicPlatform.Application.Publishing;
using ComicPlatform.Domain.Analytics;
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
            if (job.Attempts < ProcessingJob.MaxAttempts && uploads.Exists(job.UploadPath) && job.ExtraPages.All(e => uploads.Exists(e.Path)))
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
            ObjectKind.HouseCover => await db.TranslationHouses.AnyAsync(h => h.CoverKey == key, ct),
            ObjectKind.Ad => await AdsService.ReferencesAsync(db, key, ct),
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
        var kept = await db.ProcessingJobs
    .IgnoreQueryFilters([QueryFilters.Tenant])
    .Where(j => j.Status == JobStatus.Queued
        || j.Status == JobStatus.Running
        || (j.Status == JobStatus.Failed && j.FinishedAt > retryCutoff))
    .AsNoTracking()
    .Select(j => new { j.UploadPath, j.ExtraPages })
    .ToListAsync(ct);
        var keep = kept.SelectMany(j => j.ExtraPages.Select(e => e.Path).Prepend(j.UploadPath))
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

        // 3) View receipts only de-duplicate within a reporting day; keep two days of margin.
        var receiptCutoff = ReportingDay.From(now).AddDays(-2);
        await db.ViewReceipts.Where(r => r.Day < receiptCutoff).ExecuteDeleteAsync(ct);

        // 4) Hard-delete comics soft-deleted longer than the retention period.
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

/// <summary>
/// Flushes verified views every minute. One statement records the daily receipts (de-duplication that
/// survives restarts), credits only the newly receipted views to the chapter ledger at the current revenue
/// rate, and feeds the per-comic counters used by the rankings.
/// </summary>
public sealed class ViewFlushService(ViewCounter counter, NpgsqlDataSource dataSource, TimeProvider clock, ILogger<ViewFlushService> logger) : BackgroundService
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
        var batch = counter.Drain(clock.GetUtcNow());
        if (batch.IsEmpty)
        {
            return;
        }

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);
            if (batch.Accepted.Length > 0)
            {
                await FlushAcceptedAsync(connection, tx, batch.Accepted, ct);
            }

            if (batch.Suspicious.Length > 0)
            {
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO chapter_view_daily (chapter_id, day, comic_id, translation_house_id, views, suspicious_views, revenue)
                    SELECT u.chapter_id, u.day, u.comic_id, u.house_id, 0, u.n, 0
                    FROM unnest(@chapters, @comics, @houses, @days, @counts) AS u(chapter_id, comic_id, house_id, day, n)
                    ON CONFLICT (chapter_id, day) DO UPDATE SET suspicious_views = chapter_view_daily.suspicious_views + EXCLUDED.suspicious_views
                    """, connection, tx);
                cmd.Parameters.Add(Uuids("chapters", batch.Suspicious.Select(k => k.Key.ChapterId)));
                cmd.Parameters.Add(Uuids("comics", batch.Suspicious.Select(k => k.Key.ComicId)));
                cmd.Parameters.Add(Uuids("houses", batch.Suspicious.Select(k => k.Key.HouseId)));
                cmd.Parameters.Add(Days("days", batch.Suspicious.Select(k => k.Key.Day)));
                cmd.Parameters.Add(Longs("counts", batch.Suspicious.Select(k => k.Value)));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            if (batch.Rejections.Length > 0)
            {
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO view_rejection_daily (day, reason, count)
                    SELECT u.day, u.reason, u.n FROM unnest(@days, @reasons, @counts) AS u(day, reason, n)
                    ON CONFLICT (day, reason) DO UPDATE SET count = view_rejection_daily.count + EXCLUDED.count
                    """, connection, tx);
                cmd.Parameters.Add(Days("days", batch.Rejections.Select(k => k.Key.Day)));
                cmd.Parameters.Add(new NpgsqlParameter<string[]>("reasons", NpgsqlDbType.Array | NpgsqlDbType.Text) { TypedValue = batch.Rejections.Select(k => k.Key.Reason).ToArray() });
                cmd.Parameters.Add(Longs("counts", batch.Rejections.Select(k => k.Value)));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not flush {Accepted} verified views", batch.Accepted.Length);
        }
    }

    private static async Task FlushAcceptedAsync(NpgsqlConnection connection, NpgsqlTransaction tx, AcceptedView[] views, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            WITH input AS (
                SELECT * FROM unnest(@chapters, @comics, @houses, @days, @visitors) AS u(chapter_id, comic_id, house_id, day, visitor_hash)
            ),
            rate AS (
                SELECT COALESCE((SELECT (value #>> '{}')::numeric FROM system_settings WHERE key = 'revenue_per_thousand_views'), 0) AS per_thousand
            ),
            receipted AS (
                INSERT INTO view_receipts (chapter_id, day, visitor_hash, created_at)
                SELECT DISTINCT chapter_id, day, visitor_hash, now() FROM input
                ON CONFLICT DO NOTHING
                RETURNING chapter_id, day
            ),
            counted AS (
                SELECT r.chapter_id, r.day, m.comic_id, m.house_id, count(*) AS views
                FROM receipted r
                JOIN (SELECT DISTINCT chapter_id, comic_id, house_id FROM input) m ON m.chapter_id = r.chapter_id
                GROUP BY r.chapter_id, r.day, m.comic_id, m.house_id
            ),
            ledger AS (
                INSERT INTO chapter_view_daily (chapter_id, day, comic_id, translation_house_id, views, suspicious_views, revenue)
                SELECT c.chapter_id, c.day, c.comic_id, c.house_id, c.views, 0, round(c.views * rate.per_thousand / 1000, 4)
                FROM counted c CROSS JOIN rate
                ON CONFLICT (chapter_id, day) DO UPDATE SET views = chapter_view_daily.views + EXCLUDED.views,
                                                            revenue = chapter_view_daily.revenue + EXCLUDED.revenue
            )
            INSERT INTO comic_view_daily (comic_id, day, views)
            SELECT c.comic_id, c.day, sum(c.views) FROM counted c
            WHERE EXISTS (SELECT 1 FROM comics x WHERE x.id = c.comic_id)
            GROUP BY c.comic_id, c.day
            ON CONFLICT (comic_id, day) DO UPDATE SET views = comic_view_daily.views + EXCLUDED.views
            """, connection, tx);
        cmd.Parameters.Add(Uuids("chapters", views.Select(v => v.ChapterId)));
        cmd.Parameters.Add(Uuids("comics", views.Select(v => v.ComicId)));
        cmd.Parameters.Add(Uuids("houses", views.Select(v => v.HouseId)));
        cmd.Parameters.Add(Days("days", views.Select(v => v.Day)));
        cmd.Parameters.Add(new NpgsqlParameter<string[]>("visitors", NpgsqlDbType.Array | NpgsqlDbType.Text) { TypedValue = views.Select(v => v.VisitorHash).ToArray() });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlParameter<Guid[]> Uuids(string name, IEnumerable<Guid> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { TypedValue = values.ToArray() };

    private static NpgsqlParameter<DateOnly[]> Days(string name, IEnumerable<DateOnly> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Date) { TypedValue = values.ToArray() };

    private static NpgsqlParameter<long[]> Longs(string name, IEnumerable<long> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Bigint) { TypedValue = values.ToArray() };
}
