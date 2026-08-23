using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Tags;

namespace Scada.Stress;

public enum StressProfile { RuntimeBaseline, HistorianHeavy, MqttHeavy, UiActive, CombinedWorstCase }
public enum ValueChangePattern { EveryScan, EveryFourthRead, Static }
public enum RegressionVerdict { Pass, Fail, ObservationalOnly }

public sealed record StressWorkload(
    RuntimeOptions Options,
    ValueChangePattern ChangePattern,
    int Seed,
    double ExpectedTagCacheUpdatesPerSecond,
    double ExpectedValueChangesPerSecond,
    string ConfigurationHash);

public static class StressWorkloadFactory
{
    public const string WorkloadVersion = "m10-phase-a-v1";
    public const int QualificationDeviceCount = 50;
    public const int QualificationTagsPerDevice = 200;
    public const int DefaultSeed = 104729;
    private const string CanonicalShape = "m10-phase-a-v1|50|200|seed=104729|change=EveryFourthRead|Fast=100:40|Normal=500:100|Slow=1000:40|VerySlow=5000:20|types=Boolean,Int32,Int64,Double,String";

    public static StressWorkload Create(
        StressProfile profile,
        int deviceCount = QualificationDeviceCount,
        int tagsPerDevice = QualificationTagsPerDevice,
        int seed = DefaultSeed,
        ValueChangePattern changePattern = ValueChangePattern.EveryFourthRead)
    {
        if (deviceCount <= 0 || tagsPerDevice <= 0 || tagsPerDevice % 20 != 0)
            throw new ArgumentOutOfRangeException(nameof(tagsPerDevice));

        var options = new RuntimeOptions { RuntimeId = "StressRuntime" };
        options.Devices = Enumerable.Range(1, deviceCount).Select(index => new DeviceDefinition
        {
            Id = $"SIM{index:000}", Name = $"Simulator {index:000}", DriverType = "Simulator", Enabled = true,
            ConnectionOptions = new(StringComparer.OrdinalIgnoreCase) { ["StressSeed"] = seed.ToString(System.Globalization.CultureInfo.InvariantCulture), ["ChangePattern"] = changePattern.ToString() }
        }).ToList();

        var groups = new[] { ("Fast", 20), ("Normal", 50), ("Slow", 20), ("VerySlow", 10) };
        var types = Enum.GetValues<TagDataType>();
        foreach (var device in options.Devices)
        {
            var ordinal = 0;
            foreach (var (group, percent) in groups)
            {
                var count = tagsPerDevice * percent / 100;
                for (var index = 0; index < count; index++, ordinal++)
                {
                    var type = types[ordinal % types.Length];
                    options.Tags.Add(new TagDefinition
                    {
                        Id = $"{device.Id}.T{ordinal:000}", Name = $"Tag {ordinal:000}", DeviceId = device.Id,
                        Address = $"{type.ToString().ToUpperInvariant()}{ordinal:000}", DataType = type, ScanGroup = group,
                        HistoryEnabled = profile is StressProfile.HistorianHeavy or StressProfile.CombinedWorstCase,
                        HistoryProfile = type == TagDataType.Boolean ? "Digital" : "Analog",
                        MqttPublishEnabled = profile is StressProfile.MqttHeavy or StressProfile.CombinedWorstCase,
                        MqttProfile = "Stress"
                    });
                }
            }
        }

        options.Historian.Enabled = profile is StressProfile.HistorianHeavy or StressProfile.CombinedWorstCase;
        options.Mqtt.Enabled = profile is StressProfile.MqttHeavy or StressProfile.CombinedWorstCase;
        options.Mqtt.Profiles = [new() { Name = "Stress", Mode = Scada.Core.Mqtt.MqttPublishMode.OnChange, MinimumIntervalMilliseconds = 0, MaximumIntervalMilliseconds = 0 }];

        var scans = deviceCount * (tagsPerDevice * .20 * 10 + tagsPerDevice * .50 * 2 + tagsPerDevice * .20 + tagsPerDevice * .10 / 5);
        var changes = changePattern switch { ValueChangePattern.EveryScan => scans, ValueChangePattern.EveryFourthRead => scans / 4, _ => 0 };
        var descriptor = deviceCount == QualificationDeviceCount && tagsPerDevice == QualificationTagsPerDevice && seed == DefaultSeed && changePattern == ValueChangePattern.EveryFourthRead
            ? CanonicalShape
            : $"{WorkloadVersion}|{deviceCount}|{tagsPerDevice}|seed={seed}|change={changePattern}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(descriptor))).ToLowerInvariant();
        return new(options, changePattern, seed, scans, changes, hash);
    }
}

public sealed class BoundedHistogram
{
    public const int MaximumBucketCount = 64;
    private readonly long[] _buckets = new long[MaximumBucketCount];
    private long _count;
    private long _maximum;

    public long Count => Interlocked.Read(ref _count);
    public long Maximum => Interlocked.Read(ref _maximum);
    public int StorageSize => _buckets.Length;

    public void Record(long value)
    {
        value = Math.Max(0, value);
        var bucket = value == 0 ? 0 : Math.Min(MaximumBucketCount - 1, System.Numerics.BitOperations.Log2((ulong)value) + 1);
        Interlocked.Increment(ref _buckets[bucket]);
        Interlocked.Increment(ref _count);
        long current;
        while (value > (current = Interlocked.Read(ref _maximum)) && Interlocked.CompareExchange(ref _maximum, value, current) != current) { }
    }

    public long Percentile(double percentile)
    {
        if (percentile is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(percentile));
        var count = Count;
        if (count == 0) return 0;
        var target = Math.Max(1, (long)Math.Ceiling(count * percentile));
        long cumulative = 0;
        for (var index = 0; index < _buckets.Length; index++)
        {
            cumulative += Interlocked.Read(ref _buckets[index]);
            if (cumulative >= target) return index switch { 0 => 0, >= 63 => long.MaxValue, _ => 1L << index };
        }
        return Maximum;
    }
}

public sealed record ResultFingerprint(
    string MachineName, string Cpu, string Os, string DotNetVersion, int LogicalCpuCount,
    string WorkloadVersion, string Profile, string ConfigurationHash, string PowerMode,
    int Seed, int WarmupSeconds, int MeasurementSeconds)
{
    public static ResultFingerprint Example() => new("machine", "cpu", "os", Environment.Version.ToString(), 8, StressWorkloadFactory.WorkloadVersion, StressProfile.RuntimeBaseline.ToString(), "hash", "AC", StressWorkloadFactory.DefaultSeed, 60, 300);
    public string CompatibilityKey => $"{MachineName}|{Cpu}|{Os}|{DotNetVersion}|{LogicalCpuCount}|{WorkloadVersion}|{Profile}|{ConfigurationHash}|{PowerMode}|{Seed}|{WarmupSeconds}|{MeasurementSeconds}";
}

public sealed record StressMetricSummary(double CpuPercent = 0, long WorkingSetBytes = 0, double UpdatesPerSecond = 0, double ScanJitterP95Milliseconds = 0, double DispatcherP95Milliseconds = 0)
{
    public long PrivateMemoryBytes { get; init; }
    public long ManagedHeapBytes { get; init; }
    public long AllocatedBytes { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public double BatchesPerSecond { get; init; }
    public double TagsPerSecond { get; init; }
    public IReadOnlyDictionary<string, HistogramSummary> ScanDurationMicroseconds { get; init; } = new Dictionary<string, HistogramSummary>();
    public IReadOnlyDictionary<string, HistogramSummary> ScanJitterMicroseconds { get; init; } = new Dictionary<string, HistogramSummary>();
    public long MissedCycles { get; init; }
    public long PollFailures { get; init; }
    public long CallbackInvocations { get; init; }
    public long SubscriberExceptions { get; init; }
    public HistorianStressSummary? Historian { get; init; }
    public MqttStressSummary? Mqtt { get; init; }
    public DispatcherStressSummary? Dispatcher { get; init; }
    public double ShutdownMilliseconds { get; init; }
}

public sealed record HistogramSummary(long Count, long P50, long P95, long P99, long Maximum);
public sealed record HistorianStressSummary(int QueueHighWater, long Accepted, long Enqueued, long Written, long Rejected, long Dropped, long Abandoned, long WriteFailures, long BatchCount, double BatchesPerSecond, long SamplesWritten, double AverageSamplesPerBatch, HistogramSummary WriteLatencyMicroseconds, double DrainMilliseconds, long DatabaseBytes, long PersistedRows);
public sealed record MqttStressSummary(long Published, double PublishedPerSecond, int PendingHighWater, long Coalesced, long Rejected, long Failures, long Reconnects, int MaximumConcurrency, HistogramSummary PublishLatencyMicroseconds, bool LatestSequenceCorrect, long PlcReadsCaused);
public sealed record DispatcherStressSummary(long UpdateCount, int ActiveSubscriptions, HistogramSummary LatencyMicroseconds, long HeartbeatGaps);
public sealed record StressCorrectness(int DeviceCount, int TagCount, int TagValueCount, int ActiveSubscriptionsAfterShutdown, bool CleanShutdown, IReadOnlyList<string> Violations);
public sealed record StressWorkloadSummary(int DeviceCount, int TagCount, int Seed, ValueChangePattern ChangePattern, double ExpectedTagCacheUpdatesPerSecond, double ExpectedValueChangesPerSecond, double ObservedTagCacheUpdatesPerSecond, string ConfigurationHash);
public sealed record RegressionComparison(RegressionVerdict Verdict, string Reason);
public static class StressRegressionComparer
{
    public static RegressionComparison Compare(ResultFingerprint baseline, ResultFingerprint candidate, StressMetricSummary candidateMetrics) =>
        baseline.CompatibilityKey == candidate.CompatibilityKey
            ? new(RegressionVerdict.Pass, "Compatible environment; provisional gates apply.")
            : new(RegressionVerdict.ObservationalOnly, "Environment or workload fingerprint differs; automatic verdict is forbidden.");
}

public sealed record StressRunResult(
    int ResultSchemaVersion, string WorkloadVersion, StressProfile Scenario, string GitSha,
    string EnvironmentFingerprint, ResultFingerprint Fingerprint, StressWorkloadSummary Workload, StressMetricSummary Metrics, StressCorrectness Correctness,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc)
{
    public static StressRunResult CreateEmpty(StressProfile profile, ResultFingerprint fingerprint) =>
        new(1, StressWorkloadFactory.WorkloadVersion, profile, "unknown", fingerprint.CompatibilityKey, fingerprint,
            new(0, 0, fingerprint.Seed, ValueChangePattern.Static, 0, 0, 0, fingerprint.ConfigurationHash),
            new(), new(0, 0, 0, 0, false, []), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}

public static class StressResultWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    public static string Serialize(StressRunResult result) => JsonSerializer.Serialize(result, Options);

    public static async Task WriteAsync(string directory, StressRunResult result, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), Serialize(result), cancellationToken);
        var summary = $"# {result.Scenario}\n\n- Git: {result.GitSha}\n- Updates/sec: {result.Metrics.UpdatesPerSecond:F2}\n- CPU: {result.Metrics.CpuPercent:F2}%\n- Working set: {result.Metrics.WorkingSetBytes}\n- Clean shutdown: {result.Correctness.CleanShutdown}\n";
        await File.WriteAllTextAsync(Path.Combine(directory, "summary.md"), summary, cancellationToken);
        var csv = "metric,value\n" + string.Join("\n", new[]
        {
            $"cpu_percent,{result.Metrics.CpuPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"working_set_bytes,{result.Metrics.WorkingSetBytes}",
            $"tagcache_updates_per_second,{result.Metrics.UpdatesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"polling_batches_per_second,{result.Metrics.BatchesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"polling_tags_per_second,{result.Metrics.TagsPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"missed_cycles,{result.Metrics.MissedCycles}",
            $"shutdown_milliseconds,{result.Metrics.ShutdownMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        });
        await File.WriteAllTextAsync(Path.Combine(directory, "metrics.csv"), csv, cancellationToken);
    }
}
