using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Publishing;
using ComicPlatform.Infrastructure.Archives;
using ComicPlatform.Infrastructure.Configuration;
using ComicPlatform.Infrastructure.Imaging;
using ComicPlatform.Infrastructure.Packaging;
using ComicPlatform.Infrastructure.Persistence;
using ComicPlatform.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Infrastructure.Processing;

/// <summary>
/// Upload → validated archive → WebP pages on the block volume → CPK1 package (+SHA-256) → Ready.
/// Publishing (object-store upload) is a separate, explicit step. Memory stays bounded: archive entries are
/// streamed to disk, images are converted by the bounded worker pool, and the package is streamed from files.
/// </summary>
public sealed class ChapterProcessingPipeline(
    IServiceScopeFactory scopes,
    StoragePaths paths,
    ImageWorkerPool pool,
    NetVipsImageProcessor processor,
    LimitSettings limits,
    ProcessingMetrics metrics,
    IUploadStore uploads,
    IChapterFiles files,
    TimeProvider clock,
    ILogger<ChapterProcessingPipeline> logger)
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    public async Task RunAsync(Guid jobId, TimeSpan timeout, CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.ProcessingJobs.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(j => j.Id == jobId, stoppingToken);
        if (job is null || job.Status != JobStatus.Queued)
        {
            return;
        }

        var chapter = await db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]).Include(c => c.Comic).FirstOrDefaultAsync(c => c.Id == job.ChapterId, stoppingToken);
        var package = await db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(p => p.Id == job.PackageId, stoppingToken);
        if (chapter?.Comic is null || package is null)
        {
            job.Fail("comic_deleted", "Truyện hoặc chương đã bị xoá.", clock.GetUtcNow());
            await db.SaveChangesAsync(stoppingToken);
            return;
        }

        job.Start(clock.GetUtcNow());
        await db.SaveChangesAsync(stoppingToken);

        var comicId = chapter.ComicId;
        var version = package.Version;
        var workDir = paths.WorkDir(job.Id);
        var versionDir = paths.ChapterVersionDir(comicId, chapter.Id, version);
        var stopwatch = Stopwatch.StartNew();
        metrics.CurrentJobId = job.Id;
        logger.LogInformation("Processing started: job {JobId}, chapter {ChapterId}, package v{Version}, attempt {Attempt}", job.Id, chapter.Id, version, job.Attempts);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(timeout);
        var ct = timeoutCts.Token;

        try
        {
            ResetDirectory(workDir);
            ResetDirectory(versionDir);
            var pagesDir = paths.PagesDir(comicId, chapter.Id, version);
            Directory.CreateDirectory(pagesDir);

            var results = await ConvertArchiveAsync(db, job, workDir, pagesDir, ct);

            // ---- Packaging ----
            job.ReportProgress(JobStage.Packaging, results.Count, results.Count, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            var packagePath = paths.PackagePath(comicId, chapter.Id, version);
            var written = await BuildPackageAsync(results, packagePath, ct);
            logger.LogInformation("Package generated: job {JobId}, {Pages} pages, {Bytes} bytes, sha256 {Sha}", job.Id, results.Count, written.FileSize, written.Sha256Hex[..12]);

            // ---- Finalizing (commit is not cancellable: the work is done) ----
            var now = clock.GetUtcNow();
            job.ReportProgress(JobStage.Finalizing, results.Count, results.Count, now);
            var pages = results.Select(r => new ChapterPage(package.Id, chapter.Id, r.PageNumber, Path.GetFileName(r.Path), r.Width, r.Height, r.Size,
                Path.GetRelativePath(paths.ComicsRoot, r.Path).Replace('\\', '/'), r.SourceName)).ToList();
            db.ChapterPages.AddRange(pages);
            package.MarkReady(written.FileSize, written.Sha256Hex, pages);
            chapter.CompleteProcessing(now);

            var obsolete = await db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(p => p.ChapterId == chapter.Id && p.Id != package.Id && (p.Status == PackageStatus.Ready || p.Status == PackageStatus.Failed))
                .ToListAsync(CancellationToken.None);
            foreach (var old in obsolete.Where(o => o.Status == PackageStatus.Ready))
            {
                old.MarkSuperseded();
            }

            job.Succeed(now);
            await db.SaveChangesAsync(CancellationToken.None);

            uploads.Delete(job.UploadPath);
            TryDeleteDirectory(workDir);
            foreach (var old in obsolete)
            {
                files.DeleteVersion(comicId, chapter.Id, old.Version);
            }

            metrics.JobFinished(true, stopwatch.Elapsed);
            logger.LogInformation("Processing completed: job {JobId} in {Elapsed} ms", job.Id, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown: the job stays "Running" and is re-queued by recovery on the next start.
            logger.LogWarning("Processing interrupted by shutdown: job {JobId}", job.Id);
            TryDeleteDirectory(workDir);
        }
        catch (Exception ex)
        {
            var (code, message) = Classify(ex, timeout);
            if (code == "internal_error")
            {
                logger.LogError(ex, "Processing failed unexpectedly: job {JobId}", job.Id);
            }
            else
            {
                logger.LogWarning("Processing failed: job {JobId} {Code}: {Message}", job.Id, code, ex.Message);
            }

            await MarkFailedAsync(job.Id, code, message);
            TryDeleteDirectory(workDir);
            TryDeleteDirectory(versionDir);
            metrics.JobFinished(false, stopwatch.Elapsed);
        }
        finally
        {
            metrics.CurrentJobId = null;
        }
    }

    private sealed record ConvertedPage(int PageNumber, string Path, int Width, int Height, long Size, string SourceName);

    private async Task<List<ConvertedPage>> ConvertArchiveAsync(AppDbContext db, ProcessingJob job, string workDir, string pagesDir, CancellationToken ct)
    {
        await using var zipStream = new FileStream(job.UploadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var limitsForArchive = new ArchiveLimits(limits.MaxPagesPerChapter, limits.MaxExtractedSize, limits.MaxEntrySize, limits.MaxCompressionRatio);
        var entries = SafeZipReader.ValidateAndOrder(archive, limitsForArchive);

        job.ReportProgress(JobStage.Converting, 0, entries.Count, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        using var conversionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = new List<Task<ConvertedPage>>(entries.Count);
        var width = Math.Max(3, entries.Count.ToString(CultureInfo.InvariantCulture).Length);
        long extracted = 0;
        try
        {
            // Extraction is sequential (ZipArchive is not thread-safe); conversions overlap on the worker pool.
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var pageNumber = i + 1;
                var raw = Path.Combine(workDir, pageNumber.ToString(CultureInfo.InvariantCulture) + ".bin");
                extracted += await SafeZipReader.ExtractAsync(entry, raw, limits.MaxEntrySize, ct);
                if (extracted > limits.MaxExtractedSize)
                {
                    throw new PipelineException("archive_too_large", $"Tổng dung lượng ảnh sau giải nén vượt quá {limits.MaxExtractedSize / (1024 * 1024)} MB.");
                }

                var (format, animated) = await ImageSignature.DetectFileAsync(raw, ct);
                if (format == ImageFormat.Unknown)
                {
                    throw new PipelineException("unsupported_image", $"Tệp \"{PipelineException.DisplayName(entry.FullName)}\" không phải ảnh JPEG/PNG/WebP hợp lệ.");
                }

                if (animated)
                {
                    throw new PipelineException("animated_image", $"Ảnh động \"{PipelineException.DisplayName(entry.FullName)}\" không được hỗ trợ.");
                }

                var output = Path.Combine(pagesDir, pageNumber.ToString("D" + width, CultureInfo.InvariantCulture) + ".webp");
                pending.Add(ConvertAsync(pageNumber, raw, format, output, entry.FullName, conversionCts.Token));
            }

            var results = new List<ConvertedPage>(pending.Count);
            var remaining = pending.ToList();
            var lastReport = clock.GetUtcNow();
            while (remaining.Count > 0)
            {
                var finished = await Task.WhenAny(remaining);
                remaining.Remove(finished);
                results.Add(await finished);

                var now = clock.GetUtcNow();
                if (now - lastReport >= ProgressInterval || remaining.Count == 0)
                {
                    job.ReportProgress(JobStage.Converting, results.Count, entries.Count, now);
                    await db.SaveChangesAsync(ct);
                    lastReport = now;
                }
            }

            return results.OrderBy(r => r.PageNumber).ToList();
        }
        catch
        {
            await conversionCts.CancelAsync();
            await Task.WhenAll(pending.Select(t => t.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default)));
            throw;
        }
    }

    private async Task<ConvertedPage> ConvertAsync(int pageNumber, string raw, ImageFormat format, string output, string displayName, CancellationToken ct)
    {
        try
        {
            var result = await pool.RunAsync(() => processor.ConvertPage(raw, format, output, displayName), ct);
            metrics.PageConverted();
            return new ConvertedPage(pageNumber, output, result.Width, result.Height, result.Size, new string(displayName.Where(c => !char.IsControl(c)).ToArray()));
        }
        finally
        {
            TryDeleteFile(raw);
        }
    }

    private async Task<PackageWriteResult> BuildPackageAsync(IReadOnlyList<ConvertedPage> pages, string packagePath, CancellationToken ct)
    {
        var temp = packagePath + ".tmp";
        var sources = pages.Select(p => new PackagePageSource(p.PageNumber, p.Path, p.Size, p.Width, p.Height)).ToList();
        PackageWriteResult result;
        await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous))
        {
            result = await PackageWriter.WriteAsync(sources, output, ct);
            await output.FlushAsync(ct);
            output.Flush(flushToDisk: true);
        }

        // Self-check: re-parse header + index and compare with what we meant to write.
        await using (var check = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
        {
            var info = await PackageReader.ReadAsync(check, limits.MaxPagesPerChapter, verifyChecksum: false, ct);
            if (info.Entries.Count != pages.Count || info.TrailerSha256Hex != result.Sha256Hex || check.Length != result.FileSize)
            {
                throw new PackageFormatException("Package self-check failed.");
            }
        }

        File.Move(temp, packagePath, overwrite: true);
        return result;
    }

    private async Task MarkFailedAsync(Guid jobId, string code, string message)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.ProcessingJobs.IgnoreQueryFilters([QueryFilters.Tenant]).FirstAsync(j => j.Id == jobId);
            var package = await db.ChapterPackages.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(p => p.Id == job.PackageId);
            var chapter = await db.Chapters.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(c => c.Id == job.ChapterId);
            var now = clock.GetUtcNow();
            job.Fail(code, message, now);
            package?.MarkFailed();
            chapter?.FailProcessing(now);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record failure of job {JobId}", jobId);
        }
    }

    private static (string Code, string Message) Classify(Exception ex, TimeSpan timeout) => ex switch
    {
        PipelineException p => (p.Code, p.Message),
        OperationCanceledException => ("timeout", $"Xử lý chương vượt quá thời gian cho phép ({timeout.TotalMinutes:0} phút). Hãy giảm số trang hoặc dung lượng ảnh rồi thử lại."),
        InvalidDataException => ("invalid_archive", "Tệp nén bị hỏng hoặc không phải ZIP/CBZ hợp lệ."),
        PackageFormatException => ("package_failed", "Không thể đóng gói chương. Vui lòng thử lại."),
        IOException => ("io_error", "Lỗi đọc/ghi tệp trên máy chủ khi xử lý chương. Vui lòng thử lại."),
        _ => ("internal_error", "Lỗi hệ thống khi xử lý chương. Vui lòng thử lại."),
    };

    private static void ResetDirectory(string dir)
    {
        TryDeleteDirectory(dir);
        Directory.CreateDirectory(dir);
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
