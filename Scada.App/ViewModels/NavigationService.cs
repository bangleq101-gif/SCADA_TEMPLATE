using System.ComponentModel;

namespace Scada.App.ViewModels;

public sealed class NavigationService : INotifyPropertyChanged
{
    public const string OperationOverviewRoute = "operation.overview";
    public const string MachineSettingsOverviewRoute = "machine-settings.overview";
    public const string MonitoringOnlineTagsRoute = "monitoring.online-tags";
    public const string MonitoringAlarmsRoute = "monitoring.alarms";
    public const string EngineeringOverviewRoute = "engineering.overview";
    public const string EngineeringTagManagerRoute = "engineering.tag-manager";
    public const string EngineeringHistoryRoute = "engineering.history";
    public const string EngineeringMqttRoute = "engineering.mqtt";
    public const string EngineeringAlarmsRoute = "engineering.alarms";
    public const string EngineeringSystemRoute = "engineering.system";
    public const string EngineeringDiagnosticsRoute = "engineering.diagnostics";

    private readonly IReadOnlyDictionary<string, object> _pages;
    private string _currentRouteKey;
    private object _currentViewModel;

    public NavigationService(
        OperationViewModel operation,
        MachineSettingsViewModel machineSettings,
        MonitoringViewModel monitoring,
        EngineeringViewModel engineering,
        TagManagerViewModel? tagManager = null,
        HistorySettingsViewModel? historySettings = null,
        MqttSettingsViewModel? mqttSettings = null,
        AlarmMonitoringViewModel? alarmMonitoring = null,
        AlarmEngineeringViewModel? alarmEngineering = null,
        SystemServicesViewModel? systemServices = null,
        EngineeringDiagnosticsViewModel? engineeringDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(machineSettings);
        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(engineering);

        var pages = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [OperationOverviewRoute] = operation,
            [MachineSettingsOverviewRoute] = machineSettings,
            [MonitoringOnlineTagsRoute] = monitoring,
            [EngineeringOverviewRoute] = engineering
        };
        if (tagManager is not null)
        {
            pages[EngineeringTagManagerRoute] = tagManager;
        }

        if (historySettings is not null)
        {
            pages[EngineeringHistoryRoute] = historySettings;
        }
        if (mqttSettings is not null) pages[EngineeringMqttRoute] = mqttSettings;
        if (alarmMonitoring is not null) pages[MonitoringAlarmsRoute] = alarmMonitoring;
        if (alarmEngineering is not null) pages[EngineeringAlarmsRoute] = alarmEngineering;
        if (systemServices is not null) pages[EngineeringSystemRoute] = systemServices;
        if (engineeringDiagnostics is not null) pages[EngineeringDiagnosticsRoute] = engineeringDiagnostics;

        _pages = pages;

        _currentRouteKey = OperationOverviewRoute;
        _currentViewModel = operation;
        Activate(operation);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentRouteKey => _currentRouteKey;

    public object CurrentViewModel => _currentViewModel;

    public bool HasRoute(string routeKey) => _pages.ContainsKey(routeKey);

    public void Navigate(string routeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);

        if (string.Equals(routeKey, _currentRouteKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_pages.TryGetValue(routeKey, out var destination))
        {
            return;
        }

        Deactivate(_currentViewModel);

        _currentRouteKey = routeKey;
        _currentViewModel = destination;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentRouteKey)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentViewModel)));

        Activate(destination);
    }

    private static void Activate(object viewModel)
    {
        if (viewModel is IWorkspaceLifecycle lifecycle)
        {
            lifecycle.Activate();
        }
    }

    private static void Deactivate(object viewModel)
    {
        if (viewModel is IWorkspaceLifecycle lifecycle)
        {
            lifecycle.Deactivate();
        }
    }
}
