using System.Collections.Concurrent;
using System.Threading.Channels;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure.Configuration;

namespace ComicPlatform.Infrastructure.Processing;

/// <summary>
/// Bounded in-memory dispatcher. The database (processing_jobs) is the source of truth; this channel only
/// wakes the worker. Ids already waiting are not enqueued twice, so periodic DB sweeps are harmless.
/// </summary>
public sealed class ProcessingQueue : IProcessingQueue
{
    private readonly Channel<Guid> _channel;
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    public ProcessingQueue(ProcessingSettings settings)
    {
        Capacity = Math.Max(1, settings.QueueCapacity);
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public int Capacity { get; }

    public int Count => _pending.Count;

    public bool TryEnqueue(Guid jobId)
    {
        if (!_pending.TryAdd(jobId, 0))
        {
            return true; // already waiting
        }

        if (_channel.Writer.TryWrite(jobId))
        {
            return true;
        }

        _pending.TryRemove(jobId, out _);
        return false;
    }

    public async IAsyncEnumerable<Guid> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var id in _channel.Reader.ReadAllAsync(ct))
        {
            _pending.TryRemove(id, out _);
            yield return id;
        }
    }
}
