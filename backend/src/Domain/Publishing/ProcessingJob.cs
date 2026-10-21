using ComicPlatform.Domain.Catalog;

namespace ComicPlatform.Domain.Publishing;

public enum JobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Canceled = 4,
}

public enum JobStage
{
    Queued = 0,
    Validating = 1,
    Converting = 2,
    Packaging = 3,
    Finalizing = 4,
    Completed = 5,
}

public enum ExtraPagePosition
{
    Start = 0,
    End = 1,
}

/// <summary>
/// An image uploaded next to the chapter archive (e.g. the house's promotional poster) that the pipeline
/// inserts before or after the archive pages. Stored as JSON on the job; the file lives in the temp area.
/// </summary>
public sealed class ExtraPageFile
{
    public const int MaxPerUpload = 2;

    public string Path { get; set; } = null!;
    public ExtraPagePosition Position { get; set; }
    public string? FileName { get; set; }
    public long Size { get; set; }
}

public sealed class ProcessingJob
{
    public const int MaxAttempts = 3;

    private ProcessingJob()
    {
    }

    public ProcessingJob(Guid chapterId, Guid packageId, Guid requestedByUserId, string uploadPath, long uploadSize, string? originalFileName, DateTimeOffset now,
        IReadOnlyList<ExtraPageFile>? extraPages = null)
    {
        Id = Guid.CreateVersion7();
        ChapterId = chapterId;
        PackageId = packageId;
        RequestedByUserId = requestedByUserId;
        UploadPath = uploadPath;
        UploadSize = uploadSize;
        OriginalFileName = originalFileName;
        ExtraPages = extraPages?.ToList() ?? [];
        Status = JobStatus.Queued;
        Stage = JobStage.Queued;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public JobStatus Status { get; private set; }
    public JobStage Stage { get; private set; }
    public int ProgressDone { get; private set; }
    public int ProgressTotal { get; private set; }
    public int Attempts { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string UploadPath { get; private set; } = null!;
    public long UploadSize { get; private set; }
    public string? OriginalFileName { get; private set; }

    /// <summary>Promotional images inserted at the start/end of the chapter (at most <see cref="ExtraPageFile.MaxPerUpload"/>).</summary>
    public List<ExtraPageFile> ExtraPages { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? HeartbeatAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public Chapter? Chapter { get; private set; }

    public bool IsActive => Status is JobStatus.Queued or JobStatus.Running;

    public void Start(DateTimeOffset now)
    {
        Status = JobStatus.Running;
        Stage = JobStage.Validating;
        Attempts++;
        StartedAt = now;
        HeartbeatAt = now;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void ReportProgress(JobStage stage, int done, int total, DateTimeOffset now)
    {
        Stage = stage;
        ProgressDone = done;
        ProgressTotal = total;
        HeartbeatAt = now;
    }

    public void Succeed(DateTimeOffset now)
    {
        Status = JobStatus.Succeeded;
        Stage = JobStage.Completed;
        ProgressDone = ProgressTotal;
        FinishedAt = now;
        HeartbeatAt = now;
    }

    public void Fail(string code, string message, DateTimeOffset now)
    {
        Status = JobStatus.Failed;
        ErrorCode = code;
        ErrorMessage = message;
        FinishedAt = now;
        HeartbeatAt = now;
    }

    /// <summary>Puts a failed/interrupted job back into the queue (retry or crash recovery).</summary>
    public void Requeue(DateTimeOffset now)
    {
        Status = JobStatus.Queued;
        Stage = JobStage.Queued;
        ProgressDone = 0;
        FinishedAt = null;
        HeartbeatAt = now;
    }
}

public enum StorageTaskKind
{
    DeleteObject = 0,
    PurgeUrl = 1,
}

/// <summary>
/// Outbox entry for object-store side effects that must eventually happen
/// (withdrawing unpublished/superseded packages, replaced covers...).
/// </summary>
public sealed class StorageTask
{
    private StorageTask()
    {
    }

    public StorageTask(StorageTaskKind kind, string key, Guid? packageId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        Kind = kind;
        Key = key;
        PackageId = packageId;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public Guid Id { get; private set; }
    public StorageTaskKind Kind { get; private set; }
    public string Key { get; private set; } = null!;
    public Guid? PackageId { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void RecordFailure(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        var delaySeconds = Math.Min(3600, 15 * Math.Pow(2, Math.Min(Attempts, 8)));
        NextAttemptAt = now.AddSeconds(delaySeconds);
    }
}
