using Scada.Stress;
using Scada.Core.Mqtt;
using Scada.Core.Configuration;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Runtime.Mqtt;
using Scada.Runtime.Tags;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Windows.Threading;
using Xunit;

namespace Scada.Stress.Tests;

public sealed class StressContractTests
{
    [Fact]
    public void QualificationWorkloadHasExactDeterministicShapeAndFingerprint()
    {
        var first = StressWorkloadFactory.Create(StressProfile.RuntimeBaseline);
        var second = StressWorkloadFactory.Create(StressProfile.RuntimeBaseline);

        Assert.Equal(50, first.Options.Devices.Count);
        Assert.Equal(10_000, first.Options.Tags.Count);
        Assert.Equal(new[] { 2_000, 5_000, 2_000, 1_000 },
            new[] { "Fast", "Normal", "Slow", "VerySlow" }
                .Select(name => first.Options.Tags.Count(tag => tag.ScanGroup == name)));
        Assert.Equal(first.ConfigurationHash, second.ConfigurationHash);
        Assert.Equal("7aa5622677d1b19698399fd590234d17c71cb07d3c1f6e6cd59e65571fae22bf", first.ConfigurationHash);
    }

    [Fact]
    public void ChangePatternIsIndependentFromScanCadence()
    {
        var workload = StressWorkloadFactory.Create(StressProfile.RuntimeBaseline);

        Assert.Equal(ValueChangePattern.EveryFourthRead, workload.ChangePattern);
        Assert.Equal(32_200, workload.ExpectedTagCacheUpdatesPerSecond);
        Assert.Equal(8_050, workload.ExpectedValueChangesPerSecond);
    }

    [Fact]
    public void HistogramIsBoundedAndComputesPercentiles()
    {
        var histogram = new BoundedHistogram();
        for (var value = 1; value <= 100_000; value++) histogram.Record(value);

        Assert.Equal(100_000, histogram.Count);
        Assert.True(histogram.StorageSize <= BoundedHistogram.MaximumBucketCount);
        Assert.InRange(histogram.Percentile(0.50), 49_000, 66_000);
        Assert.InRange(histogram.Percentile(0.99), 98_000, 132_000);
        Assert.Equal(100_000, histogram.Maximum);
    }

    [Fact]
    public void HistogramSaturatesLargestBucketWithoutOverflow()
    {
        var histogram = new BoundedHistogram();
        histogram.Record(long.MaxValue);
        Assert.Equal(long.MaxValue, histogram.Percentile(1));
    }

    [Fact]
    public void HistogramDistinguishesTenMillisecondsFromThirteenMilliseconds()
    {
        var histogram = new BoundedHistogram();
        for (var index = 0; index < 95; index++) histogram.Record(10_000);
        for (var index = 0; index < 5; index++) histogram.Record(13_000);

        Assert.InRange(histogram.Percentile(.95), 10_000, 11_000);
        Assert.InRange(histogram.Percentile(.99), 13_000, 14_000);
    }

    [Fact]
    public void PollingJitterAggregationIsDeterministic()
    {
        var collector = new PollingMetricsCollector();
        collector.BeginMeasurement();
        collector.Record(new("Fast", 20, TimeSpan.FromMilliseconds(3), TimeSpan.FromMilliseconds(2), 1, false));
        collector.Record(new("Fast", 20, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(4), 0, true));

        var snapshot = collector.Snapshot;
        Assert.Equal(2, snapshot.Batches);
        Assert.Equal(40, snapshot.Tags);
        Assert.Equal(1, snapshot.MissedCycles);
        Assert.Equal(1, snapshot.Failures);
        Assert.Equal(4096, snapshot.Jitter["Fast"].P95);
    }

    [Fact]
    public void ComparisonRefusesIncompatibleEnvironment()
    {
        var baseline = ResultFingerprint.Example() with { MachineName = "A" };
        var candidate = baseline with { MachineName = "B" };

        var result = StressRegressionComparer.Compare(baseline, new StressMetricSummary(), candidate, new StressMetricSummary());

        Assert.Equal(RegressionVerdict.ObservationalOnly, result.Verdict);
    }

    [Fact]
    public void RegressionComparerFailsCompatibleCpuRegression()
    {
        var fingerprint = ResultFingerprint.Example();
        var result = StressRegressionComparer.Compare(
            fingerprint, new StressMetricSummary(CpuPercent: 100),
            fingerprint, new StressMetricSummary(CpuPercent: 116));

        Assert.Equal(RegressionVerdict.Fail, result.Verdict);
    }

    [Fact]
    public void RegressionComparerFailsCompatibleUpdateRateRegression()
    {
        var fingerprint = ResultFingerprint.Example();
        var result = StressRegressionComparer.Compare(
            fingerprint, new StressMetricSummary(UpdatesPerSecond: 100),
            fingerprint, new StressMetricSummary(UpdatesPerSecond: 89));

        Assert.Equal(RegressionVerdict.Fail, result.Verdict);
    }

    [Fact]
    public void RegressionComparerPassesCompatibleUnchangedCandidate()
    {
        var fingerprint = ResultFingerprint.Example();
        var metrics = new StressMetricSummary(CpuPercent: 10, WorkingSetBytes: 1000, UpdatesPerSecond: 100, ScanJitterP95Milliseconds: 10, DispatcherP95Milliseconds: 10);

        Assert.Equal(RegressionVerdict.Pass, StressRegressionComparer.Compare(fingerprint, metrics, fingerprint, metrics).Verdict);
    }

    [Fact]
    public void RegressionComparerTreatsChangedMeasurementContractAsObservationalOnly()
    {
        var baseline = ResultFingerprint.Example();
        var candidate = baseline with { MeasurementContractVersion = "m10-phase-a-v3" };

        Assert.Equal(RegressionVerdict.ObservationalOnly, StressRegressionComparer.Compare(baseline, new StressMetricSummary(), candidate, new StressMetricSummary()).Verdict);
    }

    [Fact]
    public void RegressionComparerAppliesRelativeAndNoiseFloorToJitter()
    {
        var fingerprint = ResultFingerprint.Example();
        var beneathFloor = StressRegressionComparer.Compare(
            fingerprint, new StressMetricSummary(ScanJitterP95Milliseconds: 10),
            fingerprint, new StressMetricSummary(ScanJitterP95Milliseconds: 11.9));
        var aboveFloor = StressRegressionComparer.Compare(
            fingerprint, new StressMetricSummary(ScanJitterP95Milliseconds: 10),
            fingerprint, new StressMetricSummary(ScanJitterP95Milliseconds: 12.1));

        Assert.Equal(RegressionVerdict.Pass, beneathFloor.Verdict);
        Assert.Equal(RegressionVerdict.Fail, aboveFloor.Verdict);
    }

    [Fact]
    public void RunSeriesUsesMedianAndRequiresCompatibleFingerprints()
    {
        var fingerprint = ResultFingerprint.Example();
        var runs = new[]
        {
            CreateResult(fingerprint, 100),
            CreateResult(fingerprint, 120),
            CreateResult(fingerprint, 110)
        };

        var aggregate = StressRunSeries.Aggregate(runs);

        Assert.Equal(110, aggregate.Metrics.UpdatesPerSecond);
        Assert.Equal(100, aggregate.Range.Minimum.UpdatesPerSecond);
        Assert.Equal(120, aggregate.Range.Maximum.UpdatesPerSecond);
    }

    [Fact]
    public void CorrectnessEvaluatorMakesRepresentativeHardGateViolationsFail()
    {
        var result = StressCorrectnessEvaluator.Evaluate(new StressQualificationFacts
        {
            ExpectedDeviceCount = 50,
            ActualDeviceCount = 49,
            ExpectedTagCount = 10_000,
            ActualTagCount = 10_000,
            TagCacheValueCount = 9_999,
            PollingFailures = 1,
            MissedCycles = 1,
            HistorianDropped = 1,
            MqttSourceTimestampOrderCorrect = false,
            DispatcherHeartbeatGaps = 1,
            ActiveSubscriptionsAfterShutdown = 1,
            ShutdownCompleted = false,
            UnhandledSubsystemFailure = true
        });

        Assert.False(result.Passed);
        Assert.False(result.CleanShutdown);
        Assert.NotEmpty(result.Violations);
    }

    [Fact]
    public void QualificationIdentityRejectsWrongShaAndDirtyRepository()
    {
        var verdict = StressQualificationIdentity.Evaluate(
            "D:\\SCADA\\SCADA_TEMPLATE-M10", "expected", "D:\\SCADA\\SCADA_TEMPLATE-M10", "actual", false);

        Assert.False(verdict.IsValid);
        Assert.Contains(verdict.Violations, violation => violation.Contains("SHA", StringComparison.Ordinal));
        Assert.Contains(verdict.Violations, violation => violation.Contains("dirty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MeasurementBoundaryExcludesWarmupHistoryAndMqttSamples()
    {
        var history = new TimedHistoryStore(new InMemoryHistoryStore());
        await history.WriteBatchAsync([Sample()], CancellationToken.None);
        history.BeginMeasurement();
        await history.WriteBatchAsync([Sample()], CancellationToken.None);

        await using var transport = new StressMqttTransport(TimeSpan.Zero);
        await transport.ConnectAsync(ConnectRequest(), CancellationToken.None);
        await transport.PublishAsync(Request("before"), CancellationToken.None);
        transport.BeginMeasurement();
        await transport.PublishAsync(Request("after"), CancellationToken.None);

        Assert.Equal(1, history.BatchCount);
        Assert.Equal(1, history.WriteLatency.Count);
        Assert.Equal(1, transport.MeasurementPublishedCount);
        Assert.Equal(1, transport.PublishLatency.Count);
    }

    [Fact]
    public async Task MqttTransportMarksMissingObservableOrderingFieldAsFailure()
    {
        await using var transport = new StressMqttTransport(TimeSpan.Zero);
        await transport.ConnectAsync(ConnectRequest(), CancellationToken.None);
        transport.BeginMeasurement();
        await transport.PublishAsync(new MqttPublishRequest("scada/t1", System.Text.Encoding.UTF8.GetBytes("{\"value\":1}"), MqttQualityOfService.AtMostOnce, false), CancellationToken.None);

        Assert.False(transport.SourceTimestampOrderCorrect);
    }

    [Fact]
    public void ResultContractSerializesSchemaWorkloadAndEnvironmentFingerprint()
    {
        var result = StressRunResult.CreateEmpty(StressProfile.RuntimeBaseline, ResultFingerprint.Example());
        var json = StressResultWriter.Serialize(result);

        Assert.Contains("\"resultSchemaVersion\": 2", json);
        Assert.Contains("\"measurementContractVersion\": \"m10-phase-a-v2\"", json);
        Assert.Contains("\"workloadVersion\": \"m10-phase-a-v1\"", json);
        Assert.Contains("\"environmentFingerprint\"", json);
        Assert.Contains("\"changePattern\"", json);
        Assert.Contains("\"expectedTagCacheUpdatesPerSecond\"", json);
    }

    [Fact]
    public async Task FakeMqttTransportTracksMeasurementLocalTimestampOrderLatencyAndConcurrency()
    {
        await using var transport = new StressMqttTransport(TimeSpan.FromMilliseconds(1));
        await transport.ConnectAsync(new MqttConnectRequest("localhost", 1883, MqttProtocolVersion.V311, "stress", null, null, false, 30, TimeSpan.FromSeconds(1)), CancellationToken.None);
        transport.BeginMeasurement();
        for (var sequence = 1; sequence <= 20; sequence++)
            await transport.PublishAsync(new MqttPublishRequest("scada/t1", System.Text.Encoding.UTF8.GetBytes($"{{\"value\":{sequence},\"sourceTimestampUtc\":\"2026-01-01T00:00:{sequence:00}Z\"}}"), MqttQualityOfService.AtMostOnce, false), CancellationToken.None);

        Assert.Equal(20, transport.MeasurementPublishedCount);
        Assert.True(transport.SourceTimestampOrderCorrect);
        Assert.Equal(1, transport.MaximumConcurrency);
        Assert.True(transport.PublishLatency.Count == 20);
    }

    [Fact]
    public void DispatcherProbeMeasuresRealStaDispatcher()
    {
        Exception? error = null;
        DispatcherProbeSnapshot? snapshot = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                using var probe = new DispatcherResponsivenessProbe(dispatcher);
                probe.Post();
                var frame = new DispatcherFrame();
                dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                snapshot = probe.Snapshot;
            }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot!.ExecutedCount);
        Assert.True(snapshot.LatencyMicroseconds.Count == 1);
    }

    [Fact]
    public async Task ShortRuntimeScenarioProducesObservedTagCacheRateAndCleanShutdown()
    {
        var output = Path.Combine(Path.GetTempPath(), $"scada-stress-test-{Guid.NewGuid():N}");
        try
        {
            var settings = new StressRunSettings(StressProfile.RuntimeBaseline, 2, 40, 0, 1, output, InstrumentationEnabled: true);
            var result = await new StressScenarioRunner().RunAsync(settings, CancellationToken.None);

            Assert.True(result.Metrics.UpdatesPerSecond > 0);
            Assert.Equal(80, result.Correctness.TagValueCount);
            Assert.Equal(0, result.Correctness.ActiveSubscriptionsAfterShutdown);
            Assert.True(File.Exists(Path.Combine(output, "result.json")));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Theory]
    [InlineData(StressProfile.HistorianHeavy)]
    [InlineData(StressProfile.MqttHeavy)]
    public async Task ShortSubsystemScenarioPreservesNormalCorrectnessGates(StressProfile profile)
    {
        var output = Path.Combine(Path.GetTempPath(), $"scada-stress-{profile}-{Guid.NewGuid():N}");
        try
        {
            var result = await new StressScenarioRunner().RunAsync(new(profile, 2, 40, 0, 2, output, true), CancellationToken.None);
            Assert.Empty(result.Correctness.Violations);
            if (profile == StressProfile.HistorianHeavy)
            {
                Assert.NotNull(result.Metrics.Historian);
                Assert.True(result.Metrics.Historian!.Written > 0);
                Assert.Equal(0, result.Metrics.Historian.Dropped + result.Metrics.Historian.Rejected + result.Metrics.Historian.Abandoned + result.Metrics.Historian.WriteFailures);
            }
            else
            {
                Assert.NotNull(result.Metrics.Mqtt);
                Assert.True(result.Metrics.Mqtt!.Published > 0);
                Assert.True(result.Metrics.Mqtt.Coalesced > 0);
                Assert.Equal(0, result.Metrics.Mqtt.PlcReadsCaused);
            }
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }


    [Fact]
    public async Task MqttPublisherConsumesTagCacheWithoutCallingSimulatorDriver()
    {
        var driver = new StressSimulatorDriver(ValueChangePattern.EveryScan, 7);
        var cache = new TagCache(metricsEnabled: true);
        var options = new RuntimeOptions { RuntimeId = "Stress" };
        options.Mqtt.Enabled = true;
        options.Mqtt.Profiles = [new() { Name = "Stress", Mode = MqttPublishMode.OnChange, MinimumIntervalMilliseconds = 0 }];
        options.Tags = [new() { Id = "T1", Name = "T1", DeviceId = "SIM001", MqttPublishEnabled = true, MqttProfile = "Stress", DataType = TagDataType.Int32 }];
        await using var transport = new StressMqttTransport(TimeSpan.Zero);
        await using var service = new MqttRuntimeService(options, cache, transport, NullLogger<MqttRuntimeService>.Instance, TimeProvider.System);
        await service.StartAsync(CancellationToken.None);
        transport.BeginMeasurement();
        for (var sequence = 1; sequence <= 20; sequence++) cache.Upsert(new TagUpdate("T1", sequence, TagQuality.Good, DateTimeOffset.UtcNow.AddTicks(sequence)));
        for (var attempt = 0; attempt < 100 && transport.PublishedCount == 0; attempt++) await Task.Delay(10);
        await service.StopAsync(CancellationToken.None);

        Assert.True(transport.PublishedCount > 0);
        Assert.Equal(0, driver.ReadOperations);
    }

    [Fact]
    public async Task InstrumentationOffPreservesRuntimeValues()
    {
        var output = Path.Combine(Path.GetTempPath(), $"scada-stress-off-{Guid.NewGuid():N}");
        try
        {
            var result = await new StressScenarioRunner().RunAsync(new(StressProfile.RuntimeBaseline, 1, 20, 0, 1, output, false), CancellationToken.None);
            Assert.Equal(20, result.Correctness.TagValueCount);
            Assert.Equal(0, result.Metrics.UpdatesPerSecond);
            Assert.Empty(result.Correctness.Violations);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    [Fact]
    public async Task UiActiveScenarioUsesMonitoringSubscriptionsAndRealDispatcher()
    {
        var output = Path.Combine(Path.GetTempPath(), $"scada-stress-ui-{Guid.NewGuid():N}");
        try
        {
            var result = await new StressScenarioRunner().RunAsync(new(StressProfile.UiActive, 1, 20, 0, 2, output, true), CancellationToken.None);
            Assert.NotNull(result.Metrics.Dispatcher);
            Assert.True(result.Metrics.Dispatcher!.UpdateCount > 0);
            Assert.True(result.Metrics.Dispatcher.LatencyMicroseconds.Count > 0);
            Assert.Equal(0, result.Correctness.ActiveSubscriptionsAfterShutdown);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    private static StressRunResult CreateResult(ResultFingerprint fingerprint, double updates) =>
        StressRunResult.CreateEmpty(StressProfile.RuntimeBaseline, fingerprint) with
        {
            Metrics = new StressMetricSummary(UpdatesPerSecond: updates)
        };

    private static HistorySample Sample() => new("runtime", "tag", TagDataType.Int32, 1, TagQuality.Good, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1);

    private static MqttConnectRequest ConnectRequest() => new("localhost", 1883, MqttProtocolVersion.V311, "stress", null, null, false, 30, TimeSpan.FromSeconds(1));
    private static MqttPublishRequest Request(string value) => new("scada/t1", System.Text.Encoding.UTF8.GetBytes($"{{\"value\":\"{value}\",\"sourceTimestampUtc\":\"2026-01-01T00:00:00Z\"}}"), MqttQualityOfService.AtMostOnce, false);

    private sealed class InMemoryHistoryStore : IHistoryStore
    {
        public Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken) => Task.FromResult(new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready));
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<HistorySample>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistorySample>>([]);
    }
}
