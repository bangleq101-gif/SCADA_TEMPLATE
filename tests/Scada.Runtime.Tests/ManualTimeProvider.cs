namespace Scada.Runtime.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow;
    private long _timestamp;

    public ManualTimeProvider(DateTimeOffset? utcNow = null)
    {
        _utcNow = utcNow ?? DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }

    public override long GetTimestamp()
    {
        lock (_sync)
        {
            return _timestamp;
        }
    }

    public override System.Threading.ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        lock (_sync)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.SetSchedule(dueTime, period, _utcNow);
            return timer;
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        List<ManualTimer> dueTimers;
        lock (_sync)
        {
            _utcNow = _utcNow.Add(elapsed);
            _timestamp = checked(_timestamp + (long)(elapsed.TotalSeconds * TimestampFrequency));
            dueTimers = [];
            foreach (var timer in _timers.ToArray())
            {
                while (timer.IsDue(_utcNow))
                {
                    dueTimers.Add(timer);
                    if (!timer.RepeatAfterDue(_utcNow))
                    {
                        break;
                    }
                }
            }
        }

        foreach (var timer in dueTimers)
        {
            timer.Invoke();
        }
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        lock (_sync)
        {
            _utcNow = value;
        }
    }

    private bool ChangeTimer(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (_sync)
        {
            if (timer.IsDisposed)
            {
                return false;
            }

            timer.SetSchedule(dueTime, period, _utcNow);
            return true;
        }
    }

    private void DisposeTimer(ManualTimer timer)
    {
        lock (_sync)
        {
            timer.MarkDisposed();
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer : System.Threading.ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset? _dueAtUtc;
        private TimeSpan _period;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) =>
            _owner.ChangeTimer(this, dueTime, period);

        public void Dispose() => _owner.DisposeTimer(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public bool IsDue(DateTimeOffset now) =>
            !IsDisposed && _dueAtUtc is DateTimeOffset dueAtUtc && dueAtUtc <= now;

        public bool RepeatAfterDue(DateTimeOffset now)
        {
            if (_period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero)
            {
                _dueAtUtc = null;
                return false;
            }

            _dueAtUtc = now.Add(_period);
            return true;
        }

        public void Invoke()
        {
            if (!IsDisposed)
            {
                _callback(_state);
            }
        }

        public void SetSchedule(TimeSpan dueTime, TimeSpan period, DateTimeOffset now)
        {
            _period = period;
            _dueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : now.Add(dueTime);
        }

        public void MarkDisposed() => IsDisposed = true;
    }
}
