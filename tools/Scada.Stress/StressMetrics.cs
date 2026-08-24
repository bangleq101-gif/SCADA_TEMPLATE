using System.Collections.Concurrent;
using System.Diagnostics;
using Scada.Runtime.Polling;

namespace Scada.Stress;

public sealed class PollingMetricsCollector : IPollingObserver
{
    private readonly ConcurrentDictionary<string, BoundedHistogram> _duration = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BoundedHistogram> _jitter = new(StringComparer.OrdinalIgnoreCase);
    private long _batches, _tags, _missed, _failures;
    private int _enabled;

    public void BeginMeasurement()
    {
        _duration.Clear(); _jitter.Clear();
        Interlocked.Exchange(ref _batches, 0); Interlocked.Exchange(ref _tags, 0);
        Interlocked.Exchange(ref _missed, 0); Interlocked.Exchange(ref _failures, 0);
        Volatile.Write(ref _enabled, 1);
    }

    public void Record(PollingObservation observation)
    {
        if (Volatile.Read(ref _enabled) == 0) return;
        Interlocked.Increment(ref _batches);
        Interlocked.Add(ref _tags, observation.TagCount);
        Interlocked.Add(ref _missed, observation.MissedCycles);
        if (observation.Failed) Interlocked.Increment(ref _failures);
        _duration.GetOrAdd(observation.ScanGroup, _ => new()).Record(ToMicroseconds(observation.Duration));
        _jitter.GetOrAdd(observation.ScanGroup, _ => new()).Record(ToMicroseconds(observation.StartJitter));
    }

    public PollingMetricsSnapshot Snapshot => new(
        Interlocked.Read(ref _batches), Interlocked.Read(ref _tags), Interlocked.Read(ref _missed), Interlocked.Read(ref _failures),
        _duration.ToDictionary(pair => pair.Key, pair => Summarize(pair.Value), StringComparer.OrdinalIgnoreCase),
        _jitter.ToDictionary(pair => pair.Key, pair => Summarize(pair.Value), StringComparer.OrdinalIgnoreCase));

    internal static HistogramSummary Summarize(BoundedHistogram histogram) => new(histogram.Count, histogram.Percentile(.50), histogram.Percentile(.95), histogram.Percentile(.99), histogram.Maximum);
    private static long ToMicroseconds(TimeSpan value) => Math.Max(0, value.Ticks / 10);
}

public sealed record PollingMetricsSnapshot(long Batches, long Tags, long MissedCycles, long Failures, IReadOnlyDictionary<string, HistogramSummary> Duration, IReadOnlyDictionary<string, HistogramSummary> Jitter);

public sealed class ProcessMetricsSampler
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly TimeSpan _cpuStart;
    private readonly long _allocatedStart;
    private readonly int[] _gcStart = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private long _maxWorkingSet, _maxPrivate, _maxManaged;

    public ProcessMetricsSampler()
    {
        _cpuStart = _process.TotalProcessorTime;
        _allocatedStart = GC.GetTotalAllocatedBytes(false);
    }
    public void Sample()
    {
        _process.Refresh();
        Max(ref _maxWorkingSet, _process.WorkingSet64);
        Max(ref _maxPrivate, _process.PrivateMemorySize64);
        Max(ref _maxManaged, GC.GetTotalMemory(false));
    }
    public ProcessMetricsSnapshot Snapshot()
    {
        Sample();
        var cpu = (_process.TotalProcessorTime - _cpuStart).TotalMilliseconds / Math.Max(1, _elapsed.Elapsed.TotalMilliseconds) / Environment.ProcessorCount * 100;
        return new(cpu, _maxWorkingSet, _maxPrivate, _maxManaged, GC.GetTotalAllocatedBytes(false) - _allocatedStart,
            GC.CollectionCount(0) - _gcStart[0], GC.CollectionCount(1) - _gcStart[1], GC.CollectionCount(2) - _gcStart[2]);
    }
    private static void Max(ref long target, long value) { long current; while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current) { } }
}

public sealed record ProcessMetricsSnapshot(double CpuPercent, long WorkingSet, long PrivateMemory, long ManagedHeap, long AllocatedBytes, int Gen0, int Gen1, int Gen2);
