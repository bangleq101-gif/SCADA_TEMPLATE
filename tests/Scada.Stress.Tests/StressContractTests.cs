using Scada.Stress;
using Scada.Core.Mqtt;
using Scada.Core.Configuration;
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

        var result = StressRegressionComparer.Compare(baseline, candidate, new StressMetricSummary());

        Assert.Equal(RegressionVerdict.ObservationalOnly, result.Verdict);
    }

    [Fact]
    public void ResultContractSerializesSchemaWorkloadAndEnvironmentFingerprint()
    {
        var result = StressRunResult.CreateEmpty(StressProfile.RuntimeBaseline, ResultFingerprint.Example());
        var json = StressResultWriter.Serialize(result);

        Assert.Contains("\"resultSchemaVersion\": 1", json);
        Assert.Contains("\"workloadVersion\": \"m10-phase-a-v1\"", json);
        Assert.Contains("\"environmentFingerprint\"", json);
        Assert.Contains("\"changePattern\"", json);
        Assert.Contains("\"expectedTagCacheUpdatesPerSecond\"", json);
    }

    [Fact]
    public async Task FakeMqttTransportTracksLatestSequenceLatencyAndConcurrency()
    {
        await using var transport = new StressMqttTransport(TimeSpan.FromMilliseconds(1));
        await transport.ConnectAsync(new MqttConnectRequest("localhost", 1883, MqttProtocolVersion.V311, "stress", null, null, false, 30, TimeSpan.FromSeconds(1)), CancellationToken.None);
        await Task.WhenAll(Enumerable.Range(1, 20).Select(sequence =>
            transport.PublishAsync(new MqttPublishRequest("scada/t1", System.Text.Encoding.UTF8.GetBytes($"{{\"sequence\":{sequence}}}"), MqttQualityOfService.AtMostOnce, false), CancellationToken.None)));

        Assert.Equal(20, transport.PublishedCount);
        Assert.Equal(20, transport.LatestSequenceByTopic["scada/t1"]);
        Assert.True(transport.MaximumConcurrency > 1);
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
}
