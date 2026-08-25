using Scada.App.Services;
using Scada.Runtime.Health;

namespace Scada.App.ViewModels;

public sealed class SystemServicesViewModel : RuntimeHealthWorkspaceViewModel
{
    private RuntimeHealthSnapshot? _snapshot;

    public SystemServicesViewModel(
        RuntimeHealthPresentationService health,
        IRuntimeHealthDispatcher? dispatcher = null)
        : base(health, dispatcher)
    {
        _snapshot = health.Snapshot;
    }

    public RuntimeHealthSnapshot Snapshot => _snapshot ?? LatestSnapshot;

    public IReadOnlyList<HealthServiceCard> Services =>
    [
        Card("Runtime", Snapshot.OverallState, $"Uptime {Snapshot.Uptime:g}"),
        Card("PLC", Snapshot.Plc.State, Snapshot.Plc.Summary),
        Card("TagCache", Snapshot.TagCache.MetricsAvailable ? RuntimeHealthState.Healthy : RuntimeHealthState.Unknown, $"{Snapshot.TagCache.ValueCount} values • {Snapshot.TagCache.MetricsText}"),
        Card("Historian", Map(Snapshot.Historian.State), Snapshot.Database.ProviderText),
        Card("Database", Snapshot.Database.State, Snapshot.Database.ProviderText),
        Card("MQTT", Map(Snapshot.Mqtt.State), Snapshot.Mqtt.State.ToString()),
        Card("Alarm", Map(Snapshot.Alarm.State), Snapshot.Alarm.State.ToString()),
        Card("Process", RuntimeHealthState.Healthy, $"CPU {FormatCpu(Snapshot.Process.CpuPercent)} • Working Set {FormatBytes(Snapshot.Process.WorkingSetBytes)}")
    ];

    protected override void ApplySnapshot(RuntimeHealthSnapshot snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(Services));
    }

    private static HealthServiceCard Card(string name, RuntimeHealthState state, string detail) =>
        new(name, state, detail, $"{name}: {state}. {detail}");

    private static RuntimeHealthState Map(Scada.Runtime.Historian.HistorianRuntimeState state) => state switch
    {
        Scada.Runtime.Historian.HistorianRuntimeState.Disabled => RuntimeHealthState.Disabled,
        Scada.Runtime.Historian.HistorianRuntimeState.Starting => RuntimeHealthState.Starting,
        Scada.Runtime.Historian.HistorianRuntimeState.Healthy => RuntimeHealthState.Healthy,
        Scada.Runtime.Historian.HistorianRuntimeState.Degraded => RuntimeHealthState.Degraded,
        Scada.Runtime.Historian.HistorianRuntimeState.Faulted => RuntimeHealthState.Faulted,
        _ => RuntimeHealthState.Stopping
    };

    private static RuntimeHealthState Map(Scada.Core.Mqtt.MqttRuntimeState state) => state switch
    {
        Scada.Core.Mqtt.MqttRuntimeState.Disabled => RuntimeHealthState.Disabled,
        Scada.Core.Mqtt.MqttRuntimeState.Starting or Scada.Core.Mqtt.MqttRuntimeState.Connecting => RuntimeHealthState.Starting,
        Scada.Core.Mqtt.MqttRuntimeState.Online => RuntimeHealthState.Healthy,
        Scada.Core.Mqtt.MqttRuntimeState.Offline or Scada.Core.Mqtt.MqttRuntimeState.ConfigurationRequired => RuntimeHealthState.Degraded,
        Scada.Core.Mqtt.MqttRuntimeState.Faulted => RuntimeHealthState.Faulted,
        _ => RuntimeHealthState.Stopping
    };

    private static RuntimeHealthState Map(Scada.Runtime.Alarms.AlarmRuntimeState state) => state switch
    {
        Scada.Runtime.Alarms.AlarmRuntimeState.Disabled => RuntimeHealthState.Disabled,
        Scada.Runtime.Alarms.AlarmRuntimeState.Starting => RuntimeHealthState.Starting,
        Scada.Runtime.Alarms.AlarmRuntimeState.Healthy => RuntimeHealthState.Healthy,
        Scada.Runtime.Alarms.AlarmRuntimeState.Degraded => RuntimeHealthState.Degraded,
        Scada.Runtime.Alarms.AlarmRuntimeState.Faulted => RuntimeHealthState.Faulted,
        _ => RuntimeHealthState.Stopping
    };

    private static string FormatCpu(double? value) => value is null ? "Unavailable" : $"{value.Value:0.0}%";
    private static string FormatBytes(long? value) => value is null ? "Unavailable" : $"{value.Value / 1024d / 1024d:0.0} MB";
}

public sealed record HealthServiceCard(string Name, RuntimeHealthState State, string Detail, string AutomationName);
