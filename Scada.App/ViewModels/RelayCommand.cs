using System.Windows.Input;

namespace Scada.App.ViewModels;

public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
