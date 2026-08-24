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
    public const string MeasurementContractVersion = "m10-phase-a-v3";
    public const int ResultSchemaVersion = 2;
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
    public const int SubBucketsPerPower = 8;
    public const int MaximumBucketCount = 1 + (63 * SubBucketsPerPower);
    private readonly long[] _buckets = new long[MaximumBucketCount];
    private long _count;
    private long _maximum;

    public long Count => Interlocked.Read(ref _count);
    public long Maximum => Interlocked.Read(ref _maximum);
    public int StorageSize => _buckets.Length;

    public void Record(long value)
    {
        value = Math.Max(0, value);
        var bucket = GetBucket(value);
        Interlocked.Increment(ref _buckets[bucket]);
        Interlocked.Increment(ref _count);
        long current;
        while (value > (current = Interlocked.Read(ref _maximum)) && Interlocked.CompareExchange(ref _maximum, value, current) != current) { }
    }

    public void Reset()
    {
        for (var index = 0; index < _buckets.Length; index++) Interlocked.Exchange(ref _buckets[index], 0);
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _maximum, 0);
    }

    public long Percentile(double percentile)
    {
        if (percentile is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(percentile));
        var count = Count;
        if (count == 0) return 0;
        if (percentile == 1) return Maximum;
        var target = Math.Max(1, (long)Math.Ceiling(count * percentile));
        long cumulative = 0;
        for (var index = 0; index < _buckets.Length; index++)
        {
            cumulative += Interlocked.Read(ref _buckets[index]);
            if (cumulative >= target) return BucketUpperBound(index);
        }
        return Maximum;
    }

    private static int GetBucket(long value)
    {
        if (value == 0) return 0;
        var unsignedValue = (ulong)value;
        var exponent = System.Numerics.BitOperations.Log2(unsignedValue);
        var baseValue = 1UL << exponent;
        var offset = (int)Math.Min(SubBucketsPerPower - 1, ((unsignedValue - baseValue) * SubBucketsPerPower) / baseValue);
        return 1 + (exponent * SubBucketsPerPower) + offset;
    }

    private static long BucketUpperBound(int index)
    {
        if (index == 0) return 0;
        var adjusted = index - 1;
        var exponent = adjusted / SubBucketsPerPower;
        var parts = (uint)((adjusted % SubBucketsPerPower) + 1);
        var baseValue = 1UL << exponent;
        var increment = ((baseValue / SubBucketsPerPower) * parts) + (((baseValue % SubBucketsPerPower) * parts + (SubBucketsPerPower - 1)) / SubBucketsPerPower);
        var upper = baseValue + increment;
        return upper >= long.MaxValue ? long.MaxValue : (long)upper;
    }
}

public sealed record ResultFingerprint(
    string MachineName, string Cpu, string Os, string DotNetVersion, int LogicalCpuCount,
    string WorkloadVersion, string Profile, string ConfigurationHash, string PowerMode,
    int Seed, int WarmupSeconds, int MeasurementSeconds)
{
    public string MeasurementContractVersion { get; init; } = StressWorkloadFactory.MeasurementContractVersion;
    public static ResultFingerprint Example() => new("machine", "cpu", "os", Environment.Version.ToString(), 8, StressWorkloadFactory.WorkloadVersion, StressProfile.RuntimeBaseline.ToString(), "hash", "AC", StressWorkloadFactory.DefaultSeed, 60, 300);
    public string CompatibilityKey => $"{MachineName}|{Cpu}|{Os}|{DotNetVersion}|{LogicalCpuCount}|{WorkloadVersion}|{MeasurementContractVersion}|{Profile}|{ConfigurationHash}|{PowerMode}|{Seed}|{WarmupSeconds}|{MeasurementSeconds}";
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
public sealed record HistorianStressSummary(int SampledQueueHighWater, long Accepted, long Enqueued, long ServiceWrittenSamples, long Rejected, long Dropped, long Abandoned, long WriteFailures, long BatchCount, double BatchesPerSecond, long MeasurementSamplesWritten, double AverageSamplesPerBatch, HistogramSummary WriteLatencyMicroseconds, double DrainMilliseconds, long DatabaseBytes, long PersistedRows);
public sealed record MqttStressSummary(long Published, double PublishedPerSecond, int SampledPendingHighWater, long Coalesced, long Rejected, long Failures, long Reconnects, int MaximumConcurrency, HistogramSummary PublishLatencyMicroseconds, bool SourceTimestampOrderCorrect, long PlcReadsCaused);
public sealed record DispatcherStressSummary(long UpdateCount, int ActiveSubscriptions, HistogramSummary LatencyMicroseconds, long HeartbeatGaps);
public sealed record StressCorrectness(int DeviceCount, int TagCount, int TagValueCount, int ActiveSubscriptionsAfterShutdown, bool CleanShutdown, bool Passed, IReadOnlyList<string> Violations);
public sealed record StressWorkloadSummary(int DeviceCount, int TagCount, int Seed, ValueChangePattern ChangePattern, double ExpectedTagCacheUpdatesPerSecond, double ExpectedValueChangesPerSecond, double ObservedTagCacheUpdatesPerSecond, string ConfigurationHash);
public sealed record RegressionComparison(RegressionVerdict Verdict, string Reason);
public static class StressRegressionComparer
{
    public static RegressionComparison Compare(ResultFingerprint baselineFingerprint, StressMetricSummary baseline, ResultFingerprint candidateFingerprint, StressMetricSummary candidate)
    {
        if (baselineFingerprint.CompatibilityKey != candidateFingerprint.CompatibilityKey)
            return new(RegressionVerdict.ObservationalOnly, "Environment, workload or measurement contract fingerprint differs; automatic verdict is forbidden.");

        var violations = new List<string>();
        CheckIncrease("CPU", baseline.CpuPercent, candidate.CpuPercent, .15, 0, violations);
        CheckIncrease("working set", baseline.WorkingSetBytes, candidate.WorkingSetBytes, .15, 0, violations);
        CheckDecrease("updates/sec", baseline.UpdatesPerSecond, candidate.UpdatesPerSecond, .10, violations);
        CheckIncrease("scan jitter p95", baseline.ScanJitterP95Milliseconds, candidate.ScanJitterP95Milliseconds, .20, 2, violations);
        CheckIncrease("Dispatcher p95", baseline.DispatcherP95Milliseconds, candidate.DispatcherP95Milliseconds, .20, 5, violations);
        CheckDecrease("Historian throughput", baseline.Historian?.BatchesPerSecond ?? 0, candidate.Historian?.BatchesPerSecond ?? 0, .10, violations);
        CheckIncrease("Historian sampled queue high-water", baseline.Historian?.SampledQueueHighWater ?? 0, candidate.Historian?.SampledQueueHighWater ?? 0, .20, 0, violations);
        return violations.Count == 0
            ? new(RegressionVerdict.Pass, "Compatible environment; provisional same-environment regression gates passed.")
            : new(RegressionVerdict.Fail, string.Join(" ", violations));
    }

    private static void CheckIncrease(string name, double baseline, double candidate, double ratio, double noiseFloor, List<string> violations)
    {
        if (baseline <= 0 || candidate <= baseline) return;
        if (candidate > baseline * (1 + ratio) && candidate - baseline > noiseFloor) violations.Add($"{name} exceeded the provisional regression threshold.");
    }

    private static void CheckDecrease(string name, double baseline, double candidate, double ratio, List<string> violations)
    {
        if (baseline > 0 && candidate < baseline * (1 - ratio)) violations.Add($"{name} fell below the provisional regression threshold.");
    }
}

public sealed record StressMetricRange(StressMetricSummary Minimum, StressMetricSummary Maximum);
public sealed record StressRunAggregate(ResultFingerprint Fingerprint, StressMetricSummary Metrics, StressMetricRange Range, int RunCount);
public static class StressRunSeries
{
    public static StressRunAggregate Aggregate(IReadOnlyList<StressRunResult> runs)
    {
        if (runs is null || runs.Count == 0) throw new ArgumentException("At least one run is required.", nameof(runs));
        var fingerprint = runs[0].Fingerprint;
        if (runs.Any(run => run.Fingerprint.CompatibilityKey != fingerprint.CompatibilityKey)) throw new InvalidOperationException("Run fingerprints are not compatible.");
        return new(fingerprint, AggregateMetrics(runs.Select(run => run.Metrics), Median), new(AggregateMetrics(runs.Select(run => run.Metrics), values => values.Min()), AggregateMetrics(runs.Select(run => run.Metrics), values => values.Max())), runs.Count);
    }

    private static StressMetricSummary AggregateMetrics(IEnumerable<StressMetricSummary> source, Func<double[], double> select)
    {
        var metrics = source.ToArray();
        return new(select(metrics.Select(metric => metric.CpuPercent).ToArray()), (long)select(metrics.Select(metric => (double)metric.WorkingSetBytes).ToArray()), select(metrics.Select(metric => metric.UpdatesPerSecond).ToArray()), select(metrics.Select(metric => metric.ScanJitterP95Milliseconds).ToArray()), select(metrics.Select(metric => metric.DispatcherP95Milliseconds).ToArray()));
    }

    private static double Median(double[] values) { Array.Sort(values); return values[values.Length / 2]; }
}

public sealed record StressQualificationFacts
{
    public int ExpectedDeviceCount { get; init; }
    public int ActualDeviceCount { get; init; }
    public int ExpectedTagCount { get; init; }
    public int ActualTagCount { get; init; }
    public int TagCacheValueCount { get; init; }
    public long PollingFailures { get; init; }
    public long MissedCycles { get; init; }
    public long HistorianRejected { get; init; }
    public long HistorianDropped { get; init; }
    public long HistorianAbandoned { get; init; }
    public long HistorianWriteFailures { get; init; }
    public long HistorianPersistedRows { get; init; }
    public long HistorianMeasurementSamplesWritten { get; init; }
    public bool MqttSourceTimestampOrderCorrect { get; init; } = true;
    public long MqttPlcReadsCaused { get; init; }
    public long MqttFailures { get; init; }
    public long DispatcherHeartbeatGaps { get; init; }
    public int ActiveSubscriptionsAfterShutdown { get; init; }
    public bool ShutdownCompleted { get; init; } = true;
    public bool UnhandledSubsystemFailure { get; init; }
}

public static class StressCorrectnessEvaluator
{
    public static StressCorrectness Evaluate(StressQualificationFacts facts)
    {
        var violations = new List<string>();
        if (facts.ActualDeviceCount != facts.ExpectedDeviceCount) violations.Add("Actual configured device count does not match expected count.");
        if (facts.ActualTagCount != facts.ExpectedTagCount) violations.Add("Actual configured tag count does not match expected count.");
        if (facts.TagCacheValueCount != facts.ExpectedTagCount) violations.Add("TagCache value count does not match expected tag count.");
        if (facts.PollingFailures != 0) violations.Add("Polling failures were non-zero.");
        if (facts.MissedCycles != 0) violations.Add("Missed polling cycles were non-zero.");
        if (facts.HistorianRejected != 0) violations.Add("Historian rejected samples were non-zero.");
        if (facts.HistorianDropped != 0) violations.Add("Historian dropped samples were non-zero.");
        if (facts.HistorianAbandoned != 0) violations.Add("Historian abandoned samples were non-zero.");
        if (facts.HistorianWriteFailures != 0) violations.Add("Historian write failures were non-zero.");
        if (facts.HistorianPersistedRows != facts.HistorianMeasurementSamplesWritten) violations.Add("Historian persisted row count does not match measurement-local written samples.");
        if (!facts.MqttSourceTimestampOrderCorrect) violations.Add("MQTT source-timestamp ordering verification failed.");
        if (facts.MqttPlcReadsCaused != 0) violations.Add("MQTT caused PLC reads.");
        if (facts.MqttFailures != 0) violations.Add("MQTT failures were non-zero.");
        if (facts.DispatcherHeartbeatGaps != 0) violations.Add("Dispatcher heartbeat gaps were non-zero.");
        if (facts.ActiveSubscriptionsAfterShutdown != 0) violations.Add("TagCache subscriptions remained after shutdown.");
        if (!facts.ShutdownCompleted) violations.Add("Shutdown did not complete.");
        if (facts.UnhandledSubsystemFailure) violations.Add("A subsystem ended faulted or offline during qualification.");
        var passed = violations.Count == 0;
        return new(facts.ActualDeviceCount, facts.ActualTagCount, facts.TagCacheValueCount, facts.ActiveSubscriptionsAfterShutdown, facts.ShutdownCompleted && passed, passed, violations);
    }
}

public sealed record QualificationIdentityVerdict(bool IsValid, IReadOnlyList<string> Violations);
public static class StressQualificationIdentity
{
    public static QualificationIdentityVerdict Evaluate(string expectedRepositoryRoot, string expectedSha, string actualRepositoryRoot, string actualSha, bool clean)
    {
        var violations = new List<string>();
        if (!string.Equals(Path.GetFullPath(expectedRepositoryRoot), Path.GetFullPath(actualRepositoryRoot), StringComparison.OrdinalIgnoreCase)) violations.Add("Repository root does not match the qualification root.");
        if (!string.Equals(expectedSha, actualSha, StringComparison.OrdinalIgnoreCase)) violations.Add("HEAD SHA does not match the expected qualification SHA.");
        if (!clean) violations.Add("Repository working tree is dirty.");
        return new(violations.Count == 0, violations);
    }
}

public sealed record StressRunResult(
    int ResultSchemaVersion, string WorkloadVersion, string MeasurementContractVersion, StressProfile Scenario, string GitSha, bool RepositoryClean, string RepositoryRoot,
    string EnvironmentFingerprint, ResultFingerprint Fingerprint, StressWorkloadSummary Workload, StressMetricSummary Metrics, StressCorrectness Correctness,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc)
{
    public static StressRunResult CreateEmpty(StressProfile profile, ResultFingerprint fingerprint) =>
        new(StressWorkloadFactory.ResultSchemaVersion, StressWorkloadFactory.WorkloadVersion, StressWorkloadFactory.MeasurementContractVersion, profile, "unknown", false, "unknown", fingerprint.CompatibilityKey, fingerprint,
            new(0, 0, fingerprint.Seed, ValueChangePattern.Static, 0, 0, 0, fingerprint.ConfigurationHash),
            new(), new(0, 0, 0, 0, false, false, []), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}

public static class StressResultWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    public static string Serialize(StressRunResult result) => JsonSerializer.Serialize(result, Options);

    public static async Task WriteAsync(string directory, StressRunResult result, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), Serialize(result), cancellationToken);
        var summary = $"# {result.Scenario}\n\n- Git: {result.GitSha}\n- Repository clean: {result.RepositoryClean}\n- Measurement contract: {result.MeasurementContractVersion}\n- Updates/sec: {result.Metrics.UpdatesPerSecond:F2}\n- CPU: {result.Metrics.CpuPercent:F2}%\n- Working set: {result.Metrics.WorkingSetBytes}\n- Qualification pass: {result.Correctness.Passed}\n- Clean shutdown: {result.Correctness.CleanShutdown}\n";
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
