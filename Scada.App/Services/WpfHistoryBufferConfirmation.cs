using System.Windows;

namespace Scada.App.Services;

public sealed class WpfHistoryBufferConfirmation : IHistoryBufferConfirmation
{
    public bool Confirm(string action, long count) =>
        MessageBox.Show(
            $"{action} will remove {count} pending sample(s). Continue?",
            action,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
