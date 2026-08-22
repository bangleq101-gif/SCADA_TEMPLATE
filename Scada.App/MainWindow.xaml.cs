using System.Windows;
using System.ComponentModel;
using Scada.App.Services;
using Scada.App.ViewModels;

namespace Scada.App;

public partial class MainWindow : Window
{
    private readonly ProjectEditSession _projectSession;

    public MainWindow(ShellViewModel viewModel, ProjectEditSession projectSession)
    {
        InitializeComponent();
        DataContext = viewModel;
        _projectSession = projectSession;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_projectSession.IsDirty)
        {
            return;
        }

        var result = MessageBox.Show(
            "The Tag Manager has unsaved changes. Save before closing?",
            "Unsaved project changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
        }
        else if (result == MessageBoxResult.Yes && !_projectSession.TrySave())
        {
            e.Cancel = true;
            MessageBox.Show(
                _projectSession.LastErrorMessage ?? "The project could not be saved.",
                "Save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
