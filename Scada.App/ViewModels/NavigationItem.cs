using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scada.App.ViewModels;

public sealed class NavigationItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public NavigationItem(
        string title,
        string? routeKey = null,
        IEnumerable<NavigationItem>? children = null)
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

        Title = title;
        RouteKey = string.IsNullOrWhiteSpace(routeKey) ? null : routeKey;
        Children = new ReadOnlyCollection<NavigationItem>(childItems);
        _isExpanded = Children.Count > 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string? RouteKey { get; }

    public ReadOnlyCollection<NavigationItem> Children { get; }

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
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
