namespace Scada.Runtime.Polling;

public sealed record PollingObservation(
    string ScanGroup,
    int TagCount,
    TimeSpan Duration,
    TimeSpan StartJitter,
    long MissedCycles,
    bool Failed);

public interface IPollingObserver
{
    void Record(PollingObservation observation);
}

internal sealed class NullPollingObserver : IPollingObserver
{
    public static NullPollingObserver Instance { get; } = new();
    public void Record(PollingObservation observation) { }
}
