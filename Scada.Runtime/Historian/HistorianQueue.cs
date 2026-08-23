using System.Threading.Channels;
using Scada.Core.History;

namespace Scada.Runtime.Historian;

public sealed class HistorianQueue
{
    private readonly Channel<HistorySample> _channel;
    private int _depth;

    public HistorianQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
        _channel = Channel.CreateBounded<HistorySample>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public int Capacity { get; }

    public int Depth => Math.Max(0, Volatile.Read(ref _depth));

    public bool TryWrite(HistorySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        Interlocked.Increment(ref _depth);
        if (_channel.Writer.TryWrite(sample))
        {
            return true;
        }

        Interlocked.Decrement(ref _depth);
        return false;
    }

    public async Task<IReadOnlyList<HistorySample>?> ReadBatchAsync(
        int batchSize,
        TimeSpan flushInterval,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        if (!await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var batch = new List<HistorySample>(batchSize);
        while (batch.Count < batchSize && _channel.Reader.TryRead(out var sample))
        {
            Interlocked.Decrement(ref _depth);
            batch.Add(sample);
        }

        if (batch.Count >= batchSize || flushInterval <= TimeSpan.Zero)
        {
            return batch;
        }

        using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var waitTask = _channel.Reader.WaitToReadAsync(flushCts.Token).AsTask();
        var delayTask = Task.Delay(flushInterval, flushCts.Token);
        while (batch.Count < batchSize)
        {
            var completed = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);
            if (completed == delayTask)
            {
                break;
            }

            while (batch.Count < batchSize && _channel.Reader.TryRead(out var sample))
            {
                Interlocked.Decrement(ref _depth);
                batch.Add(sample);
            }

            if (batch.Count >= batchSize || waitTask.IsCompletedSuccessfully && !waitTask.Result)
            {
                break;
            }

            waitTask = _channel.Reader.WaitToReadAsync(flushCts.Token).AsTask();
        }

        flushCts.Cancel();
        return batch;
    }

    public int Drain()
    {
        var drained = 0;
        while (_channel.Reader.TryRead(out _))
        {
            Interlocked.Decrement(ref _depth);
            drained++;
        }

        return drained;
    }

    public void Complete() => _channel.Writer.TryComplete();
}
