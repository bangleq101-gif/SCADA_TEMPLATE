using System.Windows.Controls;
using Scada.App.ViewModels;

namespace Scada.App.Views;

public partial class MachineSettingsView : UserControl
{
    public MachineSettingsView() => InitializeComponent();

    private void OnSelectedPageChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> args)
    {
        if (DataContext is MachineSettingsViewModel viewModel && args.NewValue is MachineSettingsPageViewModel page)
        {
            viewModel.SelectedPage = page;
        }
    }
}
