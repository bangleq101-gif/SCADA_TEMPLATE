using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Scada.App.Services;
using Scada.Core.Configuration;
using Scada.Runtime.Health;

namespace Scada.App.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly NavigationService _navigation;
    private readonly RuntimeOptions _options;
    private readonly RuntimeHealthPresentationService? _health;
    private readonly IRuntimeHealthDispatcher _healthDispatcher;
    private readonly object _healthSync = new();
    private IDisposable? _healthSubscription;
    private RuntimeHealthSnapshot? _healthSnapshot;
    private bool _healthDispatcherPending;
    private bool _disposed;

    public ShellViewModel(
        NavigationService navigation,
        RuntimeOptions options,
        RuntimeHealthPresentationService? health = null,
        IRuntimeHealthDispatcher? healthDispatcher = null)
    {
        _navigation = navigation;
        _options = options;
        _health = health;
        _healthDispatcher = healthDispatcher ?? new WpfRuntimeHealthDispatcher();
        _healthSnapshot = health?.Snapshot;
        _navigation.PropertyChanged += OnNavigationPropertyChanged;
        var engineeringChildren = new List<NavigationItem>
        {
            new("Engineering Overview", NavigationService.EngineeringOverviewRoute)
        };
        if (_navigation.HasRoute(NavigationService.EngineeringTagManagerRoute))
        {
            engineeringChildren.Add(new NavigationItem("Tag Manager", NavigationService.EngineeringTagManagerRoute));
        }
        if (_navigation.HasRoute(NavigationService.EngineeringHistoryRoute))
        {
            engineeringChildren.Add(new NavigationItem("History Settings", NavigationService.EngineeringHistoryRoute));
        }
        if (_navigation.HasRoute(NavigationService.EngineeringMqttRoute))
        {
            engineeringChildren.Add(new NavigationItem("MQTT Settings", NavigationService.EngineeringMqttRoute));
        }
        if (_navigation.HasRoute(NavigationService.EngineeringAlarmsRoute))
        {
            engineeringChildren.Add(new NavigationItem("Alarm Settings", NavigationService.EngineeringAlarmsRoute));
        }
        if (_navigation.HasRoute(NavigationService.EngineeringSystemRoute))
        {
            engineeringChildren.Add(new NavigationItem("System Health", NavigationService.EngineeringSystemRoute));
        }
        if (_navigation.HasRoute(NavigationService.EngineeringDiagnosticsRoute))
        {
            engineeringChildren.Add(new NavigationItem("Diagnostics", NavigationService.EngineeringDiagnosticsRoute));
        }
        if (_navigation.HasRoute(NavigationService.EngineeringDevicesRoute))
        {
            engineeringChildren.Add(new NavigationItem("Devices", NavigationService.EngineeringDevicesRoute));
        }

        var monitoringChildren = new List<NavigationItem>
        {
            new("Online Tag Monitor", NavigationService.MonitoringOnlineTagsRoute)
        };
        if (_navigation.HasRoute(NavigationService.MonitoringAlarmsRoute))
        {
            monitoringChildren.Add(new NavigationItem("Alarms", NavigationService.MonitoringAlarmsRoute));
        }

        NavigationItems =
        [
            new NavigationItem(
                "OPERATION",
                children: [new NavigationItem("Overview", NavigationService.OperationOverviewRoute)]),
            new NavigationItem(
                "MACHINE SETTINGS",
                children: [new NavigationItem("Machine Settings", NavigationService.MachineSettingsOverviewRoute)]),
            new NavigationItem(
                "MONITORING",
                children: monitoringChildren),
            new NavigationItem("ENGINEERING", children: engineeringChildren)
        ];
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is NavigationItem { IsNavigable: true, RouteKey: not null } item)
            {
                _navigation.Navigate(item.RouteKey);
            }
        });

        SynchronizeSelection();
        if (_health is not null)
        {
            _healthSubscription = _health.Subscribe(QueueHealthSnapshot);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ReadOnlyCollection<NavigationItem> NavigationItems { get; }
    public object CurrentViewModel => _navigation.CurrentViewModel;
    public string CurrentRouteKey => _navigation.CurrentRouteKey;
    public string CurrentWorkspaceTitle =>
        FindNavigationItem(_navigation.CurrentRouteKey)?.Title ?? "Workspace";
    public ICommand NavigateCommand { get; }
    public string RuntimeStatusText => $"{_options.RuntimeId}  •  {CurrentWorkspaceTitle}";
    public string HealthStatusText => _healthSnapshot is null
        ? "PLC: Unknown  •  History: Unknown  •  MQTT: Unknown  •  Runtime: Starting"
        : $"PLC: {PlcHealthState}  •  History: {HistoryHealthState}  •  MQTT: {MqttHealthState}  •  Runtime: {RuntimeHealthState}";
    public RuntimeHealthState PlcHealthState => _healthSnapshot?.Plc.State ?? RuntimeHealthState.Unknown;
    public RuntimeHealthState HistoryHealthState => _healthSnapshot is null
        ? RuntimeHealthState.Unknown
        : MapHistorian(_healthSnapshot.Historian.State);
    public RuntimeHealthState MqttHealthState => _healthSnapshot is null
        ? RuntimeHealthState.Unknown
        : MapMqtt(_healthSnapshot.Mqtt.State);
    public RuntimeHealthState RuntimeHealthState => _healthSnapshot?.OverallState ?? Scada.Runtime.Health.RuntimeHealthState.Starting;
    public string PlcHealthIndicatorText => FormatIndicator("PLC", PlcHealthState);
    public string HistoryHealthIndicatorText => FormatIndicator("History", HistoryHealthState);
    public string MqttHealthIndicatorText => FormatIndicator("MQTT", MqttHealthState);
    public string RuntimeHealthIndicatorText => FormatIndicator("Runtime", RuntimeHealthState);
    public string PlcHealthAutomationName => FormatAutomationName("PLC", PlcHealthState);
    public string HistoryHealthAutomationName => FormatAutomationName("History", HistoryHealthState);
    public string MqttHealthAutomationName => FormatAutomationName("MQTT", MqttHealthState);
    public string RuntimeHealthAutomationName => FormatAutomationName("Runtime", RuntimeHealthState);

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        SynchronizeSelection();
        OnPropertyChanged(args.PropertyName);
        if (args.PropertyName is nameof(NavigationService.CurrentRouteKey)
            or nameof(NavigationService.CurrentViewModel))
        {
            OnPropertyChanged(nameof(CurrentWorkspaceTitle));
            OnPropertyChanged(nameof(RuntimeStatusText));
        }
    }

    private void SynchronizeSelection()
    {
        foreach (var group in NavigationItems)
        {
            var selected = SynchronizeSelection(group, _navigation.CurrentRouteKey);
            if (selected)
            {
                group.IsExpanded = true;
            }
        }
    }

    private static bool SynchronizeSelection(NavigationItem item, string routeKey)
    {
        var selected = item.IsNavigable
            && string.Equals(item.RouteKey, routeKey, StringComparison.OrdinalIgnoreCase);
        item.SetSelected(selected);

        var childSelected = false;
        foreach (var child in item.Children)
        {
            childSelected |= SynchronizeSelection(child, routeKey);
        }

        if (childSelected)
        {
            item.IsExpanded = true;
        }

        return selected || childSelected;
    }

    private NavigationItem? FindNavigationItem(string routeKey)
    {
        foreach (var item in NavigationItems)
        {
            var match = FindNavigationItem(item, routeKey);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static NavigationItem? FindNavigationItem(NavigationItem item, string routeKey)
    {
        if (item.IsNavigable
            && string.Equals(item.RouteKey, routeKey, StringComparison.OrdinalIgnoreCase))
        {
            return item;
        }

        foreach (var child in item.Children)
        {
            var match = FindNavigationItem(child, routeKey);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        lock (_healthSync)
        {
            if (_disposed) return;
            _disposed = true;
            _healthSubscription?.Dispose();
            _healthSubscription = null;
        }
    }

    private void QueueHealthSnapshot(RuntimeHealthSnapshot snapshot)
    {
        lock (_healthSync)
        {
            if (_disposed) return;
            _healthSnapshot = snapshot;
            if (_healthDispatcherPending) return;
            _healthDispatcherPending = true;
        }

        _healthDispatcher.Post(DrainHealthSnapshot);
    }

    private void DrainHealthSnapshot()
    {
        lock (_healthSync)
        {
            if (_disposed)
            {
                _healthDispatcherPending = false;
                return;
            }

            _healthDispatcherPending = false;
        }

        OnPropertyChanged(nameof(HealthStatusText));
        OnPropertyChanged(nameof(PlcHealthState));
        OnPropertyChanged(nameof(HistoryHealthState));
        OnPropertyChanged(nameof(MqttHealthState));
        OnPropertyChanged(nameof(RuntimeHealthState));
        OnPropertyChanged(nameof(PlcHealthIndicatorText));
        OnPropertyChanged(nameof(HistoryHealthIndicatorText));
        OnPropertyChanged(nameof(MqttHealthIndicatorText));
        OnPropertyChanged(nameof(RuntimeHealthIndicatorText));
        OnPropertyChanged(nameof(PlcHealthAutomationName));
        OnPropertyChanged(nameof(HistoryHealthAutomationName));
        OnPropertyChanged(nameof(MqttHealthAutomationName));
        OnPropertyChanged(nameof(RuntimeHealthAutomationName));
    }

    private static string FormatIndicator(string name, RuntimeHealthState state) =>
        $"{Glyph(state)} {name}: {state}";

    private static string FormatAutomationName(string name, RuntimeHealthState state) =>
        $"{name} health: {state}";

    private static string Glyph(RuntimeHealthState state) => state switch
    {
        RuntimeHealthState.Healthy => "●",
        RuntimeHealthState.Degraded => "▲",
        RuntimeHealthState.Faulted => "■",
        RuntimeHealthState.Starting => "◌",
        RuntimeHealthState.Stopping => "■",
        RuntimeHealthState.Disabled => "—",
        _ => "?"
    };

    private static RuntimeHealthState MapHistorian(Scada.Runtime.Historian.HistorianRuntimeState state) => state switch
    {
        Scada.Runtime.Historian.HistorianRuntimeState.Disabled => RuntimeHealthState.Disabled,
        Scada.Runtime.Historian.HistorianRuntimeState.Starting => RuntimeHealthState.Starting,
        Scada.Runtime.Historian.HistorianRuntimeState.Healthy => RuntimeHealthState.Healthy,
        Scada.Runtime.Historian.HistorianRuntimeState.Degraded => RuntimeHealthState.Degraded,
        Scada.Runtime.Historian.HistorianRuntimeState.Faulted => RuntimeHealthState.Faulted,
        Scada.Runtime.Historian.HistorianRuntimeState.Stopping => RuntimeHealthState.Stopping,
        _ => RuntimeHealthState.Unknown
    };

    private static RuntimeHealthState MapMqtt(Scada.Core.Mqtt.MqttRuntimeState state) => state switch
    {
        Scada.Core.Mqtt.MqttRuntimeState.Disabled => RuntimeHealthState.Disabled,
        Scada.Core.Mqtt.MqttRuntimeState.Starting or Scada.Core.Mqtt.MqttRuntimeState.Connecting => RuntimeHealthState.Starting,
        Scada.Core.Mqtt.MqttRuntimeState.Online => RuntimeHealthState.Healthy,
        Scada.Core.Mqtt.MqttRuntimeState.Offline or Scada.Core.Mqtt.MqttRuntimeState.ConfigurationRequired => RuntimeHealthState.Degraded,
        Scada.Core.Mqtt.MqttRuntimeState.Faulted => RuntimeHealthState.Faulted,
        Scada.Core.Mqtt.MqttRuntimeState.Stopping => RuntimeHealthState.Stopping,
        _ => RuntimeHealthState.Unknown
    };
}
