using System.ComponentModel;

namespace Scada.App.ViewModels;

public sealed class NavigationService : INotifyPropertyChanged
{
    public const string OperationOverviewRoute = "operation.overview";
    public const string MachineSettingsOverviewRoute = "machine-settings.overview";
    public const string MonitoringOnlineTagsRoute = "monitoring.online-tags";
    public const string EngineeringOverviewRoute = "engineering.overview";
    public const string EngineeringTagManagerRoute = "engineering.tag-manager";

    private readonly IReadOnlyDictionary<string, object> _pages;
    private string _currentRouteKey;
    private object _currentViewModel;

    public NavigationService(
        OperationViewModel operation,
        MachineSettingsViewModel machineSettings,
        MonitoringViewModel monitoring,
        EngineeringViewModel engineering,
        TagManagerViewModel? tagManager = null)
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
