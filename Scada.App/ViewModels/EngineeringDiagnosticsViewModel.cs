using System.Collections.ObjectModel;
using Scada.App.Services;
using Scada.Runtime.Health;

namespace Scada.App.ViewModels;

public sealed class EngineeringDiagnosticsViewModel : RuntimeHealthWorkspaceViewModel
{
    private RuntimeHealthSnapshot? _snapshot;

    public EngineeringDiagnosticsViewModel(
        RuntimeHealthPresentationService health,
        IRuntimeHealthDispatcher? dispatcher = null)
        : base(health, dispatcher)
    {
        _snapshot = health.Snapshot;
        Devices = new ObservableCollection<DeviceHealthSnapshot>();
        ApplySnapshot(_snapshot);
    }

    public RuntimeHealthSnapshot Snapshot => _snapshot ?? LatestSnapshot;
    public ObservableCollection<DeviceHealthSnapshot> Devices { get; }
    public string RuntimeDetail => $"{Snapshot.RuntimeId} • {Snapshot.OverallState} • uptime {Snapshot.Uptime:g}";
    public string ProcessDetail => $"CPU: {(Snapshot.Process.CpuPercent is null ? "Unavailable" : $"{Snapshot.Process.CpuPercent:0.0}%")} • Working Set: {(Snapshot.Process.WorkingSetBytes is null ? "Unavailable" : $"{Snapshot.Process.WorkingSetBytes / 1024d / 1024d:0.0} MB")}";

    protected override void ApplySnapshot(RuntimeHealthSnapshot snapshot)
    {
        _snapshot = snapshot;
        Devices.Clear();
        foreach (var device in snapshot.Devices)
        {
            Devices.Add(device);
        }

        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(RuntimeDetail));
        OnPropertyChanged(nameof(ProcessDetail));
    }
}
