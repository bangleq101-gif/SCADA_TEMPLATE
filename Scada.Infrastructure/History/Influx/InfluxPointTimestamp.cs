namespace Scada.Infrastructure.History.Influx;

public static class InfluxPointTimestamp
{
    public const long MinNanoseconds = -9_223_372_036_854_775_806L;
    public const long MaxNanoseconds = 9_223_372_036_854_775_806L;

    private const long MinimumRepresentableDateTimeTicks = -92_233_720_368_547_758L;
    private const long MaximumRepresentableDateTimeTicks = 92_233_720_368_547_758L;

    public static bool TryGetBaseNanoseconds(DateTimeOffset recordedAtUtc, out long nanoseconds)
    {
        try
        {
            var ticksSinceEpoch = (recordedAtUtc.ToUniversalTime() - DateTimeOffset.UnixEpoch).Ticks;
            if (ticksSinceEpoch < MinimumRepresentableDateTimeTicks ||
                ticksSinceEpoch > MaximumRepresentableDateTimeTicks)
            {
                nanoseconds = 0;
                return false;
            }

            nanoseconds = ticksSinceEpoch * 100L;
            return nanoseconds is >= MinNanoseconds and <= MaxNanoseconds;
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

        if (previousNanoseconds is < MinNanoseconds or >= MaxNanoseconds)
        {
            nanoseconds = 0;
            return false;
        }

        nanoseconds = Math.Max(baseNanoseconds, previousNanoseconds.Value + 1);
        return nanoseconds is >= MinNanoseconds and <= MaxNanoseconds;
    }

    public static bool IsBelowMinimum(DateTimeOffset timestamp) =>
        GetTicksSinceEpoch(timestamp) < MinimumRepresentableDateTimeTicks;

    public static bool IsAtOrBelowMinimum(DateTimeOffset timestamp) =>
        GetTicksSinceEpoch(timestamp) <= MinimumRepresentableDateTimeTicks;

    public static bool IsAboveMaximum(DateTimeOffset timestamp) =>
        GetTicksSinceEpoch(timestamp) > MaximumRepresentableDateTimeTicks;

    private static long GetTicksSinceEpoch(DateTimeOffset timestamp) =>
        (timestamp.ToUniversalTime() - DateTimeOffset.UnixEpoch).Ticks;
}
