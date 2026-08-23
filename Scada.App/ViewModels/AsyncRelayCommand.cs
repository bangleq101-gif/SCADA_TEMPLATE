using System.Windows.Input;

namespace Scada.App.ViewModels;

public sealed class AsyncRelayCommand : ICommand, IDisposable
{
    private readonly Func<CancellationToken, Task> _execute;
    private CancellationTokenSource? _executionCts;
    private int _isRunning;
    private bool _disposed;

    public AsyncRelayCommand(Func<CancellationToken, Task> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public event EventHandler? CanExecuteChanged;

    public Task? ExecutionTask { get; private set; }

    public bool CanExecute(object? parameter) =>
        !_disposed && Volatile.Read(ref _isRunning) == 0;

    public void Execute(object? parameter)
    {
        _ = RunAsync();
    }

    public Task RunAsync()
    {
        if (!CanExecute(null))
        {
            return Task.CompletedTask;
        }

        var task = ExecuteCoreAsync();
        ExecutionTask = task;
        return task;
    }

    public void Cancel() => _executionCts?.Cancel();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _executionCts?.Cancel();
        RaiseCanExecuteChanged();
    }

    public void Refresh() => RaiseCanExecuteChanged();

    private async Task ExecuteCoreAsync()
    {
        if (Interlocked.Exchange(ref _isRunning, 1) != 0 || _disposed)
        {
            return;
        }

        RaiseCanExecuteChanged();
        using var executionCts = new CancellationTokenSource();
        _executionCts = executionCts;
        try
        {
            await _execute(executionCts.Token);
        }
        catch (OperationCanceledException) when (executionCts.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // The command observes the task so fire-and-forget ICommand execution
            // cannot create an unobserved exception. The owning ViewModel reports
            // operation errors in its own status surface.
        }
        finally
        {
            _executionCts = null;
            Interlocked.Exchange(ref _isRunning, 0);
            RaiseCanExecuteChanged();
        }
    }

    private void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
