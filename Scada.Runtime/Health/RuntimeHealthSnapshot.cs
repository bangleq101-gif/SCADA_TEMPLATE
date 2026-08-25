using Scada.Core.Alarms;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.History;
using Scada.Core.Mqtt;
using Scada.Runtime.Alarms;
using Scada.Runtime.Historian;

namespace Scada.Runtime.Health;

public sealed record DeviceHealthSnapshot(
    string DeviceId,
    RuntimeHealthState State,
    DeviceConnectionState ConnectionState,
    string? LastError,
    DateTimeOffset? LastSuccessfulRead,
    DateTimeOffset? LastFailureAt,
    long ReadCount,
    long FailureCount,
    DateTimeOffset? LastScanStartedAt,
    DateTimeOffset? LastScanCompletedAt,
    TimeSpan? LastScanDuration,
    long MissedCycleCount);

public sealed record PlcHealthSnapshot(
    RuntimeHealthState State,
    int EnabledDeviceCount,
    int HealthyDeviceCount,
    int StartingDeviceCount,
    int DegradedDeviceCount,
    int FaultedDeviceCount,
    string? LastErrorCode,
    string? LastErrorMessage)
{
    public string Summary => EnabledDeviceCount == 0
        ? "Not configured"
        : $"{HealthyDeviceCount}/{EnabledDeviceCount} healthy";
}

public sealed record TagCacheHealthSnapshot(
    int ValueCount,
    int SubscriptionCount,
    bool MetricsAvailable,
    long? Updates,
    long? CallbackInvocations,
    long? SubscriberExceptions)
{
    public string MetricsText => MetricsAvailable ? "Available" : "Unavailable / metrics disabled";
}

public sealed record DatabaseHealthSnapshot(
    HistoryStorageProvider Provider,
    RuntimeHealthState State,
    bool DiagnosticsAvailable,
    HistoryStoreDiagnosticsSnapshot? Diagnostics,
    string? LastErrorCode,
    string? LastErrorMessage)
{
    public string ProviderText => Provider == HistoryStorageProvider.InfluxDb2 ? "InfluxDB 2.x" : "SQLite";
}

public sealed record RuntimeHealthSnapshot(
    string RuntimeId,
    DateTimeOffset CapturedAtUtc,
    TimeSpan Uptime,
    RuntimeHealthState OverallState,
    ProcessTelemetrySnapshot Process,
    PlcHealthSnapshot Plc,
    TagCacheHealthSnapshot TagCache,
    HistorianRuntimeSnapshot Historian,
    DatabaseHealthSnapshot Database,
    MqttRuntimeSnapshot Mqtt,
    AlarmRuntimeSnapshot Alarm,
    IReadOnlyList<DeviceHealthSnapshot> Devices)
{
    public static RuntimeHealthSnapshot Starting(
        RuntimeOptions options,
        DateTimeOffset capturedAtUtc,
        HistorianRuntimeSnapshot historian,
        MqttRuntimeSnapshot mqtt,
        AlarmRuntimeSnapshot alarm,
        TagCacheHealthSnapshot tagCache,
        DatabaseHealthSnapshot database) => new(
        options.RuntimeId,
        capturedAtUtc,
        TimeSpan.Zero,
        RuntimeHealthState.Starting,
        ProcessTelemetrySnapshot.Unavailable,
        new PlcHealthSnapshot(
            RuntimeHealthState.Starting,
            options.Devices.Count(device => device.Enabled && !string.IsNullOrWhiteSpace(device.Id)),
            0,
            0,
            0,
            0,
            null,
            null),
        tagCache,
        historian,
        database,
        mqtt,
        alarm,
        []);
}
