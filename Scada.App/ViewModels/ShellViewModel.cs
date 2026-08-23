using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Scada.Core.Configuration;

namespace Scada.App.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly NavigationService _navigation;
    private readonly RuntimeOptions _options;

    public ShellViewModel(NavigationService navigation, RuntimeOptions options)
    {
        _navigation = navigation;
        _options = options;
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
                children: [new NavigationItem("Online Tag Monitor", NavigationService.MonitoringOnlineTagsRoute)]),
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ReadOnlyCollection<NavigationItem> NavigationItems { get; }
    public object CurrentViewModel => _navigation.CurrentViewModel;
    public string CurrentRouteKey => _navigation.CurrentRouteKey;
    public string CurrentWorkspaceTitle =>
        FindNavigationItem(_navigation.CurrentRouteKey)?.Title ?? "Workspace";
    public ICommand NavigateCommand { get; }
    public string RuntimeStatusText => $"{_options.RuntimeId}  •  {CurrentWorkspaceTitle}";

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
}
