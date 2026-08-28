using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scada.App.Screens;

namespace Scada.App.ViewModels;

public sealed class NavigationItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public NavigationItem(
        string title,
        string? routeKey = null,
        IEnumerable<NavigationItem>? children = null,
        ScreenDescriptor? screen = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var childItems = (children ?? []).ToArray();
        if (childItems.Length == 0 && string.IsNullOrWhiteSpace(routeKey))
        {
            throw new ArgumentException("A leaf navigation item must have a route key.", nameof(routeKey));
        }

        if (childItems.Length > 0 && !string.IsNullOrWhiteSpace(routeKey))
        {
            throw new ArgumentException("A group navigation item cannot have a route key.", nameof(routeKey));
        }

        if (screen is not null && childItems.Length > 0)
        {
            throw new ArgumentException("A screen navigation item cannot have children.", nameof(screen));
        }

        if (screen is not null
            && !string.Equals(screen.RouteKey, routeKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Screen metadata must use the navigation item's route key.", nameof(screen));
        }

        Title = title;
        RouteKey = string.IsNullOrWhiteSpace(routeKey) ? null : routeKey;
        Children = new ReadOnlyCollection<NavigationItem>(childItems);
        Screen = screen;
        _isExpanded = Children.Count > 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string? RouteKey { get; }

    public ReadOnlyCollection<NavigationItem> Children { get; }

    // RouteKey remains the navigation authority. Screen is immutable display metadata.
    public ScreenDescriptor? Screen { get; }

    public string? ScreenId => Screen?.ScreenId;

    public ScreenCategory? Category => Screen?.Category;

    public string? IconKey => Screen?.IconKey;

    public int? Order => Screen?.Order;

    public string? RequiredRole => Screen?.RequiredRole;

    public bool IsGroup => Children.Count > 0;

    public bool IsNavigable => Children.Count == 0 && RouteKey is not null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    internal void SetSelected(bool selected) => IsSelected = selected;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
