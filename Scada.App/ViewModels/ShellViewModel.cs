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
        _navigation.PropertyChanged += (_, args) => OnPropertyChanged(args.PropertyName);
        NavigationItems =
        [
            new("operation", "OPERATION", "Overview"),
            new("machine-settings", "MACHINE SETTINGS", "Machine Settings"),
            new("monitoring", "MONITORING", "Online Tag Monitor"),
            new("engineering", "ENGINEERING", "Engineering")
        ];
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is NavigationItem item)
            {
                _navigation.Navigate(item.Key);
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ReadOnlyCollection<NavigationItem> NavigationItems { get; }
    public object CurrentViewModel => _navigation.CurrentViewModel;
    public ICommand NavigateCommand { get; }
    public string RuntimeStatusText => $"{_options.RuntimeId}  •  Foundation";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
