using Scada.Core.Alarms;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.History;
using Scada.Core.Mqtt;
using Scada.Runtime.Alarms;
using Scada.Runtime.Devices;
using Scada.Runtime.Historian;
using Scada.Runtime.Tags;

namespace Scada.Runtime.Health;

public sealed class RuntimeHealthAggregator
{
    private readonly RuntimeOptions _options;

    public RuntimeHealthAggregator(RuntimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public RuntimeHealthSnapshot Aggregate(
        DateTimeOffset capturedAtUtc,
        TimeSpan uptime,
        IReadOnlyDictionary<string, DeviceRuntimeSnapshot> deviceSnapshots,
        TagCacheRuntimeSnapshot tagCache,
        HistorianRuntimeSnapshot historian,
        MqttRuntimeSnapshot mqtt,
        AlarmRuntimeSnapshot alarm,
        HistoryStoreDiagnosticsSnapshot? databaseDiagnostics,
        ProcessTelemetrySnapshot process)
    {
        var devices = deviceSnapshots
            .Values
            .OrderBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(CreateDevice)
            .ToArray();
        var plc = CreatePlc(devices);
        var safeHistorian = historian with
        {
            LastErrorMessage = RuntimeHealthSanitizer.Sanitize(historian.LastErrorMessage)
        };
        var safeDatabaseDiagnostics = databaseDiagnostics is null
            ? null
            : databaseDiagnostics with
            {
                LastErrorMessage = RuntimeHealthSanitizer.Sanitize(databaseDiagnostics.LastErrorMessage)
            };
        var database = CreateDatabase(safeHistorian, safeDatabaseDiagnostics);
        var tagCacheSnapshot = new TagCacheHealthSnapshot(
            tagCache.ValueCount,
            tagCache.SubscriptionCount,
            tagCache.MetricsAvailable,
            tagCache.MetricsAvailable ? tagCache.Updates : null,
            tagCache.MetricsAvailable ? tagCache.CallbackInvocations : null,
            tagCache.MetricsAvailable ? tagCache.SubscriberExceptions : null);

        return new RuntimeHealthSnapshot(
            _options.RuntimeId,
            capturedAtUtc,
            uptime,
            SelectOverallState(plc.State, database.State, MapMqtt(mqtt.State), MapAlarm(alarm.State)),
            process,
            plc,
            tagCacheSnapshot,
            safeHistorian,
            database,
            mqtt with
            {
                LastErrorMessage = RuntimeHealthSanitizer.Sanitize(mqtt.LastErrorMessage)
            },
            alarm with
            {
                LastErrorMessage = RuntimeHealthSanitizer.Sanitize(alarm.LastErrorMessage)
            },
            devices);
    }

    private DeviceHealthSnapshot CreateDevice(DeviceRuntimeSnapshot snapshot)
    {
        var state = snapshot.ConnectionState switch
        {
            DeviceConnectionState.Faulted => RuntimeHealthState.Faulted,
            DeviceConnectionState.Connecting => RuntimeHealthState.Starting,
            DeviceConnectionState.Connected when snapshot.LastSuccessfulRead is not null => RuntimeHealthState.Healthy,
            DeviceConnectionState.Connected => RuntimeHealthState.Starting,
            DeviceConnectionState.Disconnected when snapshot.LastFailureAt is not null
                || snapshot.FailureCount > 0
                || snapshot.LastSuccessfulRead is not null
                || !string.IsNullOrWhiteSpace(snapshot.LastError) => RuntimeHealthState.Degraded,
            _ => RuntimeHealthState.Unknown
        };

        return new DeviceHealthSnapshot(
            snapshot.DeviceId,
            state,
            snapshot.ConnectionState,
            RuntimeHealthSanitizer.Sanitize(snapshot.LastError),
            snapshot.LastSuccessfulRead,
            snapshot.LastFailureAt,
            snapshot.ReadCount,
            snapshot.FailureCount,
            snapshot.LastScanStartedAt,
            snapshot.LastScanCompletedAt,
            snapshot.LastScanDuration,
            snapshot.MissedCycleCount);
    }

    private PlcHealthSnapshot CreatePlc(IReadOnlyList<DeviceHealthSnapshot> devices)
    {
        var enabledIds = _options.Devices
            .Where(device => device.Enabled && !string.IsNullOrWhiteSpace(device.Id))
            .Select(device => device.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabledDevices = devices
            .Where(device => enabledIds.Contains(device.DeviceId))
            .ToArray();
        var representedIds = enabledDevices
            .Select(device => device.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabled = enabledIds.Count;
        var healthy = enabledDevices.Count(device => device.State == RuntimeHealthState.Healthy);
        var starting = enabledDevices.Count(device => device.State is RuntimeHealthState.Starting or RuntimeHealthState.Unknown)
            + enabled - representedIds.Count;
        var degraded = enabledDevices.Count(device => device.State == RuntimeHealthState.Degraded);
        var faulted = enabledDevices.Count(device => device.State == RuntimeHealthState.Faulted);
        var state = enabled == 0
            ? RuntimeHealthState.Unknown
            : faulted > 0
                ? RuntimeHealthState.Faulted
                : degraded > 0
                    ? RuntimeHealthState.Degraded
                    : starting > 0
                        ? RuntimeHealthState.Starting
                        : RuntimeHealthState.Healthy;
        var error = devices.FirstOrDefault(device => !string.IsNullOrWhiteSpace(device.LastError));
        return new PlcHealthSnapshot(
            state,
            enabled,
            healthy,
            starting,
            degraded,
            faulted,
            error is null ? null : "PLC_DEVICE",
            error?.LastError);
    }

    private DatabaseHealthSnapshot CreateDatabase(
        HistorianRuntimeSnapshot historian,
        HistoryStoreDiagnosticsSnapshot? diagnostics)
    {
        var provider = _options.Historian.StorageProvider;
        var state = diagnostics is null
            ? MapHistorian(historian.State)
            : MapHistoryStore(diagnostics.State);
        var errorCode = diagnostics?.LastErrorCode ?? historian.LastErrorCode;
        var errorMessage = RuntimeHealthSanitizer.Sanitize(diagnostics?.LastErrorMessage ?? historian.LastErrorMessage);
        return new DatabaseHealthSnapshot(provider, state, diagnostics is not null, diagnostics, errorCode, errorMessage);
    }

    private static RuntimeHealthState MapHistorian(HistorianRuntimeState state) => state switch
    {
        HistorianRuntimeState.Disabled => RuntimeHealthState.Disabled,
        HistorianRuntimeState.Starting => RuntimeHealthState.Starting,
        HistorianRuntimeState.Healthy => RuntimeHealthState.Healthy,
        HistorianRuntimeState.Degraded => RuntimeHealthState.Degraded,
        HistorianRuntimeState.Faulted => RuntimeHealthState.Faulted,
        HistorianRuntimeState.Stopping => RuntimeHealthState.Stopping,
        _ => RuntimeHealthState.Unknown
    };

    private static RuntimeHealthState MapHistoryStore(HistoryStoreState state) => state switch
    {
        HistoryStoreState.Disabled => RuntimeHealthState.Disabled,
        HistoryStoreState.Starting or HistoryStoreState.Connecting => RuntimeHealthState.Starting,
        HistoryStoreState.Online => RuntimeHealthState.Healthy,
        HistoryStoreState.Offline or HistoryStoreState.Buffering or HistoryStoreState.Resynchronizing or HistoryStoreState.ConfigurationRequired => RuntimeHealthState.Degraded,
        HistoryStoreState.Faulted => RuntimeHealthState.Faulted,
        HistoryStoreState.Stopping => RuntimeHealthState.Stopping,
        _ => RuntimeHealthState.Unknown
    };

    private static RuntimeHealthState MapMqtt(MqttRuntimeState state) => state switch
    {
        MqttRuntimeState.Disabled => RuntimeHealthState.Disabled,
        MqttRuntimeState.Starting or MqttRuntimeState.Connecting => RuntimeHealthState.Starting,
        MqttRuntimeState.Online => RuntimeHealthState.Healthy,
        MqttRuntimeState.Offline or MqttRuntimeState.ConfigurationRequired => RuntimeHealthState.Degraded,
        MqttRuntimeState.Faulted => RuntimeHealthState.Faulted,
        MqttRuntimeState.Stopping => RuntimeHealthState.Stopping,
        _ => RuntimeHealthState.Unknown
    };

    private static RuntimeHealthState MapAlarm(AlarmRuntimeState state) => state switch
    {
        AlarmRuntimeState.Disabled => RuntimeHealthState.Disabled,
        AlarmRuntimeState.Starting => RuntimeHealthState.Starting,
        AlarmRuntimeState.Healthy => RuntimeHealthState.Healthy,
        AlarmRuntimeState.Degraded => RuntimeHealthState.Degraded,
        AlarmRuntimeState.Faulted => RuntimeHealthState.Faulted,
        AlarmRuntimeState.Stopping => RuntimeHealthState.Stopping,
        _ => RuntimeHealthState.Unknown
    };

    private static RuntimeHealthState SelectOverallState(params RuntimeHealthState[] states)
    {
        if (states.Any(state => state == RuntimeHealthState.Stopping)) return RuntimeHealthState.Stopping;
        if (states.Any(state => state == RuntimeHealthState.Faulted)) return RuntimeHealthState.Faulted;
        if (states.Any(state => state == RuntimeHealthState.Degraded)) return RuntimeHealthState.Degraded;
        if (states.Any(state => state == RuntimeHealthState.Starting)) return RuntimeHealthState.Starting;
        if (states.Any(state => state == RuntimeHealthState.Healthy)) return RuntimeHealthState.Healthy;
        return RuntimeHealthState.Unknown;
    }
}
