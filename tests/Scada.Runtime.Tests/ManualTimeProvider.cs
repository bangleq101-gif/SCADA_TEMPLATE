namespace Scada.Runtime.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    private long _timestamp;

    public ManualTimeProvider(DateTimeOffset? utcNow = null)
    {
        _utcNow = utcNow ?? DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan elapsed)
    {
        _utcNow = _utcNow.Add(elapsed);
        _timestamp += checked((long)(elapsed.TotalSeconds * TimestampFrequency));
    }

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
}
