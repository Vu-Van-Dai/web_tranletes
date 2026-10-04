using System.Collections.Concurrent;
using ComicPlatform.Infrastructure.Configuration;

namespace ComicPlatform.Infrastructure.Processing;

/// <summary>
/// Runs CPU-bound image work on exactly N dedicated background threads (N = IMAGE_PROCESSING_CONCURRENCY).
/// Keeping this work off the ThreadPool means a busy conversion queue cannot starve API requests on a
/// 2-OCPU host. Work is queued in an unbounded FIFO; callers bound how much they submit.
/// </summary>
public sealed class ImageWorkerPool : IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>());
    private readonly Thread[] _threads;
    private readonly ProcessingMetrics _metrics;

    public ImageWorkerPool(ProcessingSettings settings, ProcessingMetrics metrics)
    {
        _metrics = metrics;
        Concurrency = Math.Max(1, settings.ImageConcurrency);
        _threads = new Thread[Concurrency];
        for (var i = 0; i < Concurrency; i++)
        {
            _threads[i] = new Thread(Loop)
            {
                IsBackground = true,
                Name = $"image-worker-{i + 1}",
                Priority = ThreadPriority.BelowNormal,
            };
            _threads[i].Start();
        }
    }

    public int Concurrency { get; }

    public int QueueLength => _queue.Count;

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(new WorkItem(() =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return;
            }

            try
            {
                tcs.TrySetResult(work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }), ct);
        return tcs.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        foreach (var t in _threads)
        {
            t.Join(TimeSpan.FromSeconds(5));
        }

        _queue.Dispose();
    }

    private void Loop()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            _metrics.ConversionStarted();
            try
            {
                item.Execute();
            }
            finally
            {
                _metrics.ConversionFinished();
            }
        }
    }

    private sealed record WorkItem(Action Execute);
}

/// <summary>Process-wide processing counters (exposed to admins and asserted by tests).</summary>
public sealed class ProcessingMetrics
{
    private int _active;
    private int _maxObserved;
    private long _jobsSucceeded;
    private long _jobsFailed;
    private long _pagesConverted;
    private long _lastJobMilliseconds;

    public int ActiveConversions => Volatile.Read(ref _active);
    public int MaxObservedConversions => Volatile.Read(ref _maxObserved);
    public long JobsSucceeded => Interlocked.Read(ref _jobsSucceeded);
    public long JobsFailed => Interlocked.Read(ref _jobsFailed);
    public long PagesConverted => Interlocked.Read(ref _pagesConverted);
    public long LastJobMilliseconds => Interlocked.Read(ref _lastJobMilliseconds);
    public Guid? CurrentJobId { get; set; }

    public void ConversionStarted()
    {
        var now = Interlocked.Increment(ref _active);
        int seen;
        while (now > (seen = Volatile.Read(ref _maxObserved)) && Interlocked.CompareExchange(ref _maxObserved, now, seen) != seen)
        {
        }
    }

    public void ConversionFinished() => Interlocked.Decrement(ref _active);

    public void PageConverted() => Interlocked.Increment(ref _pagesConverted);

    public void JobFinished(bool success, TimeSpan elapsed)
    {
        Interlocked.Increment(ref success ? ref _jobsSucceeded : ref _jobsFailed);
        Interlocked.Exchange(ref _lastJobMilliseconds, (long)elapsed.TotalMilliseconds);
    }

    public void ResetMaxObserved() => Interlocked.Exchange(ref _maxObserved, Volatile.Read(ref _active));
}
