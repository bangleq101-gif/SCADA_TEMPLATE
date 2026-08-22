using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Scada.App.ViewModels;

namespace Scada.App.Views;

public partial class TagManagerView : UserControl
{
    public TagManagerView() => InitializeComponent();

    private void TagGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is TagManagerViewModel viewModel)
        {
            viewModel.SetSelection(TagGrid.SelectedItems.Cast<object>());
        }
    }

    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TagManagerViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            viewModel.ImportCsv(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            MessageBox.Show(exception.Message, "CSV import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TagManagerViewModel viewModel)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var source = TagGrid.SelectedItems.Count > 0
                ? TagGrid.SelectedItems.Cast<TagEditorRowViewModel>()
                : null;
            viewModel.ExportCsv(dialog.FileName, source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(exception.Message, "CSV export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
