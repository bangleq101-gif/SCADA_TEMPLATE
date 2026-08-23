namespace Scada.Infrastructure.History.Influx;

public static class InfluxPointTimestamp
{
    public static bool TryGetBaseNanoseconds(DateTimeOffset recordedAtUtc, out long nanoseconds)
    {
        try
        {
            var ticksSinceEpoch = (recordedAtUtc.ToUniversalTime() - DateTimeOffset.UnixEpoch).Ticks;
            nanoseconds = checked(ticksSinceEpoch * 100L);
            return true;
        }
        catch (OverflowException)
        {
            nanoseconds = 0;
            return false;
        }
    }

    public static bool TryAllocate(
        DateTimeOffset recordedAtUtc,
        long? previousNanoseconds,
        out long nanoseconds)
    {
        if (!TryGetBaseNanoseconds(recordedAtUtc, out var baseNanoseconds))
        {
            nanoseconds = 0;
            return false;
        }

        if (previousNanoseconds is null)
        {
            nanoseconds = baseNanoseconds;
            return true;
        }

        if (previousNanoseconds == long.MaxValue)
        {
            nanoseconds = 0;
            return false;
        }

        nanoseconds = Math.Max(baseNanoseconds, previousNanoseconds.Value + 1);
        return true;
    }
}
