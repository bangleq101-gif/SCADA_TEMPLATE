using Microsoft.Extensions.Logging.Abstractions;
using Scada.Core.Alarms;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.History;
using Scada.Core.Mqtt;
using Scada.Runtime.Alarms;
using Scada.Runtime.Health;
using Scada.Runtime.Historian;
using Scada.Runtime.Mqtt;
using Scada.Runtime.Tags;
using Scada.Runtime.Devices;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class RuntimeHealthAggregatorTests
{
    [Fact]
    public void HealthyDeviceAndDisabledOptionalServicesProduceHealthyOverallState()
    {
        var options = Options(new DeviceDefinition { Id = "PLC-1", Enabled = true });
        var snapshot = Aggregate(options, new DeviceRuntimeSnapshot(
            "PLC-1", DeviceConnectionState.Connected, null, DateTimeOffset.UtcNow, null,
            4, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(4), 0));

        Assert.Equal(RuntimeHealthState.Healthy, snapshot.OverallState);
        Assert.Equal(RuntimeHealthState.Healthy, snapshot.Plc.State);
        Assert.Equal("1/1 healthy", snapshot.Plc.Summary);
        Assert.Single(snapshot.Devices);
    }

    [Fact]
    public void FaultedPrecedesDegradedAndStarting()
    {
        var options = Options(
            new DeviceDefinition { Id = "PLC-A", Enabled = true },
            new DeviceDefinition { Id = "PLC-B", Enabled = true });
        var snapshots = new Dictionary<string, DeviceRuntimeSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["PLC-A"] = new("PLC-A", DeviceConnectionState.Disconnected, "password=secret", null, DateTimeOffset.UtcNow, 0, 1, null, null, null, 0),
            ["PLC-B"] = new("PLC-B", DeviceConnectionState.Faulted, "fault", null, DateTimeOffset.UtcNow, 0, 1, null, null, null, 0)
        };

        var snapshot = Aggregate(options, snapshots);

        Assert.Equal(RuntimeHealthState.Faulted, snapshot.OverallState);
        Assert.Equal(RuntimeHealthState.Faulted, snapshot.Plc.State);
        Assert.DoesNotContain("secret", snapshot.Plc.LastErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoEnabledDeviceIsUnknownRatherThanHealthy()
    {
        var snapshot = Aggregate(new RuntimeOptions(), new Dictionary<string, DeviceRuntimeSnapshot>());

        Assert.Equal(RuntimeHealthState.Unknown, snapshot.OverallState);
        Assert.Equal("Not configured", snapshot.Plc.Summary);
    }

    [Fact]
    public void MissingEnabledDeviceSnapshotIsStartingRatherThanHealthy()
    {
        var options = Options(new DeviceDefinition { Id = "PLC-1", Enabled = true });

        var snapshot = Aggregate(options, new Dictionary<string, DeviceRuntimeSnapshot>());

        Assert.Equal(RuntimeHealthState.Starting, snapshot.Plc.State);
        Assert.Equal(RuntimeHealthState.Starting, snapshot.OverallState);
        Assert.Equal("0/1 healthy", snapshot.Plc.Summary);
    }

    [Fact]
    public void DisabledTagCacheMetricsAreExplicitlyUnavailable()
    {
        var options = new RuntimeOptions();
        var cache = new TagCacheRuntimeSnapshot(90, 40, 3, 11, 12)
        {
            MetricsAvailable = false
        };

        var snapshot = Aggregate(options, new Dictionary<string, DeviceRuntimeSnapshot>(), cache);

        Assert.False(snapshot.TagCache.MetricsAvailable);
        Assert.Null(snapshot.TagCache.Updates);
        Assert.Null(snapshot.TagCache.CallbackInvocations);
        Assert.Null(snapshot.TagCache.SubscriberExceptions);
        Assert.Contains("Unavailable", snapshot.TagCache.MetricsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InfluxDiagnosticsEnrichDatabaseWithoutChangingRuntimeBoundary()
    {
        var options = new RuntimeOptions();
        options.Historian.StorageProvider = HistoryStorageProvider.InfluxDb2;
        var diagnostics = new HistoryStoreDiagnosticsSnapshot(
            HistoryStoreState.Online, 2, 3, 4, 5, 6, 7, 8, 9, DateTimeOffset.UtcNow, null, null);

        var snapshot = Aggregate(options, new Dictionary<string, DeviceRuntimeSnapshot>(), databaseDiagnostics: diagnostics);

        Assert.Equal(HistoryStorageProvider.InfluxDb2, snapshot.Database.Provider);
        Assert.True(snapshot.Database.DiagnosticsAvailable);
        Assert.Equal(RuntimeHealthState.Healthy, snapshot.Database.State);
        Assert.Equal(diagnostics, snapshot.Database.Diagnostics);
    }

    [Fact]
    public void FaultedInfluxDiagnosticsContributeToOverallState()
    {
        var options = new RuntimeOptions
        {
            Historian = { StorageProvider = HistoryStorageProvider.InfluxDb2 }
        };
        var diagnostics = new HistoryStoreDiagnosticsSnapshot(
            HistoryStoreState.Faulted, 0, 0, 0, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, "STORE", "unavailable");

        var snapshot = Aggregate(options, new Dictionary<string, DeviceRuntimeSnapshot>(), databaseDiagnostics: diagnostics);

        Assert.Equal(RuntimeHealthState.Faulted, snapshot.Database.State);
        Assert.Equal(RuntimeHealthState.Faulted, snapshot.OverallState);
    }

    [Fact]
    public void ProcessCpuCalculatorUsesMonotonicDeltaAndProcessorNormalization()
    {
        var previous = new ProcessTelemetryReading(TimeSpan.FromSeconds(1), 100);
        var current = new ProcessTelemetryReading(TimeSpan.FromSeconds(2), 200);

        var cpu = ProcessTelemetryCalculator.Calculate(previous, 0, current, 4_000, 4_000, 4);

        Assert.Equal(25d, cpu);
        Assert.Null(ProcessTelemetryCalculator.Calculate(previous, 4_000, current, 4_000, 4_000, 4));
    }

    [Fact]
    public void SanitizerRedactsCredentialsAndLocalPathsBeforeAppBoundary()
    {
        var safe = RuntimeHealthSanitizer.Sanitize(
            "connect failed mqtt://operator:password@broker Password=secret Token=abc Path=C:\\Secrets\\mqtt.json");

        Assert.NotNull(safe);
        Assert.DoesNotContain("operator", safe!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", safe!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc", safe!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Secrets", safe!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AggregatedHistorianAndStoreDiagnosticsAreSanitizedBeforePublication()
    {
        var options = new RuntimeOptions();
        var historian = new HistorianRuntimeSnapshot(
            HistorianRuntimeState.Faulted, 1, 0, 0, 0, 0, 0, 0, 0, null, "STORE", "Password=secret");
        var diagnostics = new HistoryStoreDiagnosticsSnapshot(
            HistoryStoreState.Faulted, 0, 0, 0, 0, 0, 0, 0, 0, null, "STORE", "Token=abc");
        var mqtt = new MqttRuntimeSnapshot(MqttRuntimeState.Disabled, 0, 0, 0, 0, 0, 0, 0, null, null, null, null);
        var alarm = AlarmRuntimeSnapshot.Disabled;

        var snapshot = new RuntimeHealthAggregator(options).Aggregate(
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            new Dictionary<string, DeviceRuntimeSnapshot>(),
            new TagCacheRuntimeSnapshot(0, 0, 0, 0, 0),
            historian,
            mqtt,
            alarm,
            diagnostics,
            ProcessTelemetrySnapshot.Unavailable);

        Assert.DoesNotContain("secret", snapshot.Historian.LastErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc", snapshot.Database.Diagnostics?.LastErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeHealthSnapshot Aggregate(
        RuntimeOptions options,
        DeviceRuntimeSnapshot device,
        TagCacheRuntimeSnapshot? cache = null) =>
        Aggregate(options, new Dictionary<string, DeviceRuntimeSnapshot> { [device.DeviceId] = device }, cache);

    private static RuntimeHealthSnapshot Aggregate(
        RuntimeOptions options,
        IReadOnlyDictionary<string, DeviceRuntimeSnapshot> devices,
        TagCacheRuntimeSnapshot? cache = null,
        HistoryStoreDiagnosticsSnapshot? databaseDiagnostics = null)
    {
        var historian = new HistorianRuntimeSnapshot(HistorianRuntimeState.Disabled, 0, 0, 0, 0, 0, 0, 0, 0, null, null, null);
        var mqtt = new MqttRuntimeSnapshot(MqttRuntimeState.Disabled, 0, 0, 0, 0, 0, 0, 0, null, null, null, null);
        var alarm = AlarmRuntimeSnapshot.Disabled;
        return new RuntimeHealthAggregator(options).Aggregate(
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(5),
            devices,
            cache ?? new TagCacheRuntimeSnapshot(0, 0, 0, 0, 0),
            historian,
            mqtt,
            alarm,
            databaseDiagnostics,
            new ProcessTelemetrySnapshot(12.5, 1_024, true));
    }

    private static RuntimeOptions Options(params DeviceDefinition[] devices) => new() { Devices = [.. devices] };
}
