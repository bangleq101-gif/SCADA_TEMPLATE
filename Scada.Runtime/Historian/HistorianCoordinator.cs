namespace Scada.Runtime.Historian;

public sealed class HistorianCoordinator(TimeProvider timeProvider)
{
    private readonly object _sync = new();
    private readonly PriorityQueue<ScheduledTag, long> _due = new();
    private readonly Dictionary<string, long> _scheduled = new(StringComparer.OrdinalIgnoreCase);

    public void Schedule(string tagId, long dueTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);

        lock (_sync)
        {
            _scheduled[tagId] = dueTimestamp;
            _due.Enqueue(new ScheduledTag(tagId, dueTimestamp), dueTimestamp);
        }
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
            while (_due.TryPeek(out var scheduled, out var priority))
            {
                if (_scheduled.TryGetValue(scheduled.TagId, out var current) && current == priority)
                {
                    var elapsed = timeProvider.GetElapsedTime(now, priority);
                    return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
                }

                _due.Dequeue();
            }
        }

        return TimeSpan.FromMilliseconds(250);
    }

    private sealed record ScheduledTag(string TagId, long DueTimestamp);
}
