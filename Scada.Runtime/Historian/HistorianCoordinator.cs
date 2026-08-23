namespace Scada.Runtime.Historian;

public sealed class HistorianCoordinator(TimeProvider timeProvider) : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _scheduleChanged = new(0, 1);
    private readonly PriorityQueue<ScheduledTag, long> _due = new();
    private readonly Dictionary<string, long> _scheduled = new(StringComparer.OrdinalIgnoreCase);

    public void Schedule(string tagId, long dueTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);

        var shouldSignal = false;
        lock (_sync)
        {
            var hadEarliest = TryGetEarliestDueLocked(out var previousEarliest);
            _scheduled[tagId] = dueTimestamp;
            _due.Enqueue(new ScheduledTag(tagId, dueTimestamp), dueTimestamp);
            shouldSignal = !hadEarliest || dueTimestamp < previousEarliest;
        }

        if (shouldSignal)
        {
            try
            {
                _scheduleChanged.Release();
            }
            catch (SemaphoreFullException)
            {
                // A wakeup is already pending; one signal is sufficient.
            }
        }
    }

    public async Task WaitForNextAsync(long now, CancellationToken cancellationToken)
    {
        var delay = GetDelay(now);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, timeProvider, waitCts.Token);
        var changedTask = _scheduleChanged.WaitAsync(waitCts.Token);
        await Task.WhenAny(delayTask, changedTask).ConfigureAwait(false);
        waitCts.Cancel();

        try
        {
            await Task.WhenAll(delayTask, changedTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (waitCts.IsCancellationRequested)
        {
            // The timer or signal wait was cancelled after the other wait won.
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public bool TryTakeDue(long now, out string? tagId)
    {
        lock (_sync)
        {
            while (_due.TryPeek(out var scheduled, out var priority) && priority <= now)
            {
                _due.Dequeue();
                if (_scheduled.TryGetValue(scheduled.TagId, out var current) && current == priority)
                {
                    _scheduled.Remove(scheduled.TagId);
                    tagId = scheduled.TagId;
                    return true;
                }
            }
        }

        tagId = null;
        return false;
    }

    public TimeSpan GetDelay(long now)
    {
        lock (_sync)
        {
            if (TryGetEarliestDueLocked(out var priority))
            {
                var elapsed = timeProvider.GetElapsedTime(now, priority);
                return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            }
        }

        return TimeSpan.FromMilliseconds(250);
    }

    public void Dispose() => _scheduleChanged.Dispose();

    private bool TryGetEarliestDueLocked(out long priority)
    {
        while (_due.TryPeek(out var scheduled, out priority))
        {
            if (_scheduled.TryGetValue(scheduled.TagId, out var current) && current == priority)
            {
                return true;
            }

            _due.Dequeue();
        }

        priority = 0;
        return false;
    }

    private sealed record ScheduledTag(string TagId, long DueTimestamp);
}
