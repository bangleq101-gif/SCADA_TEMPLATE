using System.Diagnostics;
using System.Windows.Threading;

namespace Scada.Stress;

public sealed class DispatcherResponsivenessProbe(Dispatcher dispatcher) : IDisposable
{
    private readonly BoundedHistogram _latency = new();
    private long _posted, _executed, _heartbeatGaps;
    public void Post()
    {
        var started = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _posted);
        dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            var microseconds = (long)(Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000);
            _latency.Record(microseconds);
            if (microseconds > 100_000) Interlocked.Increment(ref _heartbeatGaps);
            Interlocked.Increment(ref _executed);
        }));
    }
    public DispatcherProbeSnapshot Snapshot => new(Interlocked.Read(ref _posted), Interlocked.Read(ref _executed), Interlocked.Read(ref _heartbeatGaps), _latency);
    public void Dispose() { }
}

public sealed record DispatcherProbeSnapshot(long PostedCount, long ExecutedCount, long HeartbeatGaps, BoundedHistogram LatencyMicroseconds);
