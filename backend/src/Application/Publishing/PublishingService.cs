using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Auditing;
using ComicPlatform.Application.Catalog;
using ComicPlatform.Application.Common;
using ComicPlatform.Application.Tenancy;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Application.Publishing;

public sealed class PublishingOptions
{
    public long MaxUploadSize { get; init; }
    public TimeSpan PublishUploadTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Chapter lifecycle commands: upload (→ processing queue), publish (→ object store, then DB),
/// unpublish (→ outbox withdrawal) and retry.
/// </summary>
public sealed class PublishingService(
    IAppDbContext db,
    ITenantContext tenant,
    TenantResourceGuard guard,
    IUploadStore uploads,
    IProcessingQueue queue,
    IObjectStore store,
    IStoragePaths paths,
    IChapterFiles files,
    IDistributedLock locks,
    StorageOutbox outbox,
    ComicStatsUpdater stats,
    HouseChapterService chapters,
    IAuditLogger audit,
    PublishingOptions options,
    TimeProvider clock,
    ILogger<PublishingService> logger)
{
    public void EnsureQueueAccepting()
    {
        if (queue.Count >= queue.Capacity)
        {
            throw Errors.Unavailable("queue_full", "Hàng đợi xử lý đang đầy, vui lòng thử lại sau ít phút.");
        }
    }

    public async Task<JobSummary> AcceptUploadAsync(Guid chapterId, Stream content, string? fileName, CancellationToken ct)
    {
        var chapter = await guard.GetChapterAsync(chapterId, ct);
        await EnsureNoActiveJobAsync(chapterId, ct);
        EnsureQueueAccepting();

        var uploadId = Guid.CreateVersion7();
        logger.LogInformation("Upload started for chapter {ChapterId} by user {UserId}", chapterId, tenant.UserId);
        var saved = await uploads.SaveAsync(uploadId, content, options.MaxUploadSize, ct);

        ProcessingJob job;
        try
        {
            var now = clock.GetUtcNow();
            var version = chapter.BeginProcessing(now);
            var package = new ChapterPackage(chapter.Id, version, ObjectKeys.Package(chapter.ComicId, chapter.Id, version), now);
            job = new ProcessingJob(chapter.Id, package.Id, tenant.UserId, saved.Path, saved.Size, SafeFileName(fileName), now);
            db.ChapterPackages.Add(package);
            db.ProcessingJobs.Add(job);
            audit.Add(AuditActions.ChapterUploaded, "chapter", chapter.Id, tenant.HouseId, new { version, saved.Size });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            uploads.Delete(saved.Path);
            throw;
        }

        logger.LogInformation("Upload completed for chapter {ChapterId}: {Bytes} bytes, job {JobId}", chapterId, saved.Size, job.Id);
        if (!queue.TryEnqueue(job.Id))
        {
            await FailQueuedJobAsync(job.Id, ct);
            throw Errors.Unavailable("queue_full", "Hàng đợi xử lý đang đầy, vui lòng thử lại sau ít phút.");
        }

        return HouseChapterService.ToSummary(job);
    }

    public async Task<HouseChapterDetail> PublishAsync(Guid chapterId, CancellationToken ct)
    {
        var chapter = await guard.GetChapterAsync(chapterId, ct);
        await EnsureNoActiveJobAsync(chapterId, ct);

        var publishedVersion = chapter.PublishedPackageId is null
            ? null
            : await db.ChapterPackages.Where(p => p.Id == chapter.PublishedPackageId).Select(p => (int?)p.Version).FirstOrDefaultAsync(ct);
        var package = await db.ChapterPackages
            .Where(p => p.ChapterId == chapterId && p.Status == PackageStatus.Ready && (publishedVersion == null || p.Version > publishedVersion))
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct)
            ?? throw Errors.Conflict("chapter_not_ready", "Chương chưa có bản đã xử lý xong để đăng.");

        await using (await locks.AcquireAsync(LockKey(package.Id), ct))
        {
            if (!files.PackageExists(chapter.ComicId, chapter.Id, package.Version))
            {
                logger.LogError("Package file missing for package {PackageId}", package.Id);
                throw Errors.Conflict("package_missing", "Không tìm thấy tệp gói chương trên máy chủ, vui lòng tải lên lại.");
            }

            await EnsureInStoreAsync(package, chapter, ct);

            var now = clock.GetUtcNow();
            ChapterPackage? previous = null;
            if (chapter.PublishedPackageId is { } prevId && prevId != package.Id)
            {
                previous = await db.ChapterPackages.FirstOrDefaultAsync(p => p.Id == prevId, ct);
                previous?.MarkSuperseded();
                if (previous is not null)
                {
                    outbox.Withdraw(previous.StorageKey, previous.Id);
                }
            }

            package.MarkPublished(now);
            chapter.Publish(package.Id, package.PageCount, now);
            await stats.RecomputeAsync(chapter.Comic!, ct);
            audit.Add(AuditActions.ChapterPublished, "chapter", chapter.Id, tenant.HouseId, new { package.Version, superseded = previous?.Version });
            await db.SaveChangesAsync(ct);

            if (previous is not null)
            {
                files.DeleteVersion(chapter.ComicId, chapter.Id, previous.Version);
            }
        }

        logger.LogInformation("Chapter {ChapterId} published (package v{Version})", chapterId, package.Version);
        return await chapters.GetAsync(chapterId, ct);
    }

    public async Task<HouseChapterDetail> UnpublishAsync(Guid chapterId, CancellationToken ct)
    {
        var chapter = await guard.GetChapterAsync(chapterId, ct);
        if (chapter.Status != ChapterStatus.Published || chapter.PublishedPackageId is null)
        {
            throw Errors.Conflict("chapter_not_published", "Chương chưa được đăng.");
        }

        var package = await db.ChapterPackages.FirstAsync(p => p.Id == chapter.PublishedPackageId, ct);
        await using (await locks.AcquireAsync(LockKey(package.Id), ct))
        {
            var now = clock.GetUtcNow();
            chapter.Unpublish(now);
            package.MarkUnpublished();
            outbox.Withdraw(package.StorageKey, package.Id);
            await stats.RecomputeAsync(chapter.Comic!, ct);
            audit.Add(AuditActions.ChapterUnpublished, "chapter", chapter.Id, tenant.HouseId, new { package.Version });
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Chapter {ChapterId} unpublished", chapterId);
        return await chapters.GetAsync(chapterId, ct);
    }

    public async Task<JobSummary> RetryAsync(Guid chapterId, CancellationToken ct)
    {
        var chapter = await guard.GetChapterAsync(chapterId, ct);
        var job = await db.ProcessingJobs.Where(j => j.ChapterId == chapterId).OrderByDescending(j => j.CreatedAt).FirstOrDefaultAsync(ct);
        if (job is null || job.Status != JobStatus.Failed)
        {
            throw Errors.Conflict("nothing_to_retry", "Không có lần xử lý thất bại nào để thử lại.");
        }

        if (!uploads.Exists(job.UploadPath))
        {
            throw Errors.Conflict("upload_expired", "Tệp tải lên đã hết hạn lưu trữ, vui lòng tải chương lên lại.");
        }

        EnsureQueueAccepting();
        var package = await db.ChapterPackages.FirstAsync(p => p.Id == job.PackageId, ct);
        var now = clock.GetUtcNow();
        package.ResetForRetry();
        chapter.RetryProcessing(now);
        job.Requeue(now);
        audit.Add(AuditActions.ChapterRetried, "chapter", chapter.Id, tenant.HouseId, new { job.Id });
        await db.SaveChangesAsync(ct);

        if (!queue.TryEnqueue(job.Id))
        {
            await FailQueuedJobAsync(job.Id, ct);
            throw Errors.Unavailable("queue_full", "Hàng đợi xử lý đang đầy, vui lòng thử lại sau ít phút.");
        }

        logger.LogInformation("Processing retry queued for chapter {ChapterId}, job {JobId}", chapterId, job.Id);
        return HouseChapterService.ToSummary(job);
    }

    public static string LockKey(Guid packageId) => "package:" + packageId.ToString("N");

    private async Task EnsureInStoreAsync(ChapterPackage package, Chapter chapter, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.PublishUploadTimeout);
        try
        {
            var head = await store.HeadAsync(package.StorageKey, timeout.Token);
            if (head is not null && head.Size == package.FileSize)
            {
                return;
            }

            var path = paths.PackagePath(chapter.ComicId, chapter.Id, package.Version);
            await store.PutFileAsync(package.StorageKey, path, "application/octet-stream", package.Checksum, timeout.Token);
            head = await store.HeadAsync(package.StorageKey, timeout.Token);
            if (head is null || head.Size != package.FileSize)
            {
                throw new IOException($"Object store verification failed for {package.StorageKey}.");
            }

            logger.LogInformation("Package uploaded to {Store}: {Key} ({Bytes} bytes)", store.Name, package.StorageKey, package.FileSize);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            logger.LogError(ex, "Uploading package {PackageId} to {Store} failed", package.Id, store.Name);
            throw Errors.UpstreamFailed("store_upload_failed", "Không thể tải gói chương lên kho lưu trữ. Vui lòng thử lại.");
        }
    }

    private async Task EnsureNoActiveJobAsync(Guid chapterId, CancellationToken ct)
    {
        if (await db.ProcessingJobs.AnyAsync(j => j.ChapterId == chapterId && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running), ct))
        {
            throw Errors.Conflict("chapter_processing", "Chương đang được xử lý, vui lòng đợi hoàn tất.");
        }
    }

    private async Task FailQueuedJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.ProcessingJobs.FirstAsync(j => j.Id == jobId, ct);
        var chapter = await db.Chapters.FirstAsync(c => c.Id == job.ChapterId, ct);
        var package = await db.ChapterPackages.FirstAsync(p => p.Id == job.PackageId, ct);
        var now = clock.GetUtcNow();
        job.Fail("queue_full", "Hàng đợi xử lý đang đầy. Hãy bấm \"Thử lại\" sau ít phút.", now);
        package.MarkFailed();
        chapter.FailProcessing(now);
        await db.SaveChangesAsync(ct);
    }

    private static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var file = Path.GetFileName(name.Replace('\\', '/').Split('/').Last());
        var clean = new string(file.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > 200 ? clean[..200] : clean;
    }
}
