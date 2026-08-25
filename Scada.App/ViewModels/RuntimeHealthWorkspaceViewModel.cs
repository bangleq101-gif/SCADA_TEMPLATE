using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scada.App.Services;
using Scada.Runtime.Health;

namespace Scada.App.ViewModels;

public abstract class RuntimeHealthWorkspaceViewModel : IWorkspaceLifecycle, IDisposable, INotifyPropertyChanged
{
    private readonly object _sync = new();
    private readonly RuntimeHealthPresentationService _health;
    private readonly IRuntimeHealthDispatcher _dispatcher;
    private IDisposable? _subscription;
    private RuntimeHealthSnapshot? _pendingSnapshot;
    private bool _dispatcherPending;
    private long _generation;
    private bool _active;
    private bool _disposed;

    protected RuntimeHealthWorkspaceViewModel(
        RuntimeHealthPresentationService health,
        IRuntimeHealthDispatcher? dispatcher = null)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _dispatcher = dispatcher ?? new WpfRuntimeHealthDispatcher();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsActive
    {
        get { lock (_sync) return _active && !_disposed; }
    }

    public int OwnedHealthSubscriptionCount
    {
        get { lock (_sync) return _subscription is null ? 0 : 1; }
    }

    internal int PendingHealthDispatcherUpdateCount
    {
        get { lock (_sync) return _dispatcherPending ? 1 : 0; }
    }

    protected RuntimeHealthSnapshot LatestSnapshot => _health.Snapshot;

    public void Activate()
    {
        long generation;
        lock (_sync)
        {
            if (_disposed || _active)
            {
                return;
            }

            _active = true;
            generation = ++_generation;
        }

        var subscription = _health.Subscribe(snapshot => QueueSnapshot(snapshot, generation));
        lock (_sync)
        {
            if (_disposed || !_active || generation != _generation)
            {
                subscription.Dispose();
                return;
            }

            _subscription = subscription;
        }
    }

    public void Deactivate()
    {
        IDisposable? subscription;
        lock (_sync)
        {
            _active = false;
            ++_generation;
            subscription = _subscription;
            _subscription = null;
            _pendingSnapshot = null;
            _dispatcherPending = false;
        }

        subscription?.Dispose();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Deactivate();
    }

    protected abstract void ApplySnapshot(RuntimeHealthSnapshot snapshot);

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void QueueSnapshot(RuntimeHealthSnapshot snapshot, long generation)
    {
        lock (_sync)
        {
            if (!IsCurrentLocked(generation))
            {
                return;
            }

            _pendingSnapshot = snapshot;
            if (_dispatcherPending)
            {
                return;
            }

            _dispatcherPending = true;
        }

        _dispatcher.Post(() => DrainSnapshot(generation));
    }

    private void DrainSnapshot(long generation)
    {
        RuntimeHealthSnapshot? snapshot;
        lock (_sync)
        {
            if (!IsCurrentLocked(generation))
            {
                return;
            }

            snapshot = _pendingSnapshot;
            _pendingSnapshot = null;
            if (snapshot is null)
            {
                _dispatcherPending = false;
                return;
            }
        }

        if (IsCurrent(generation))
        {
            ApplySnapshot(snapshot);
        }

        lock (_sync)
        {
            if (!IsCurrentLocked(generation))
            {
                return;
            }

            if (_pendingSnapshot is null)
            {
                _dispatcherPending = false;
                return;
            }
        }

        _dispatcher.Post(() => DrainSnapshot(generation));
    }

    private bool IsCurrent(long generation)
    {
        lock (_sync) return IsCurrentLocked(generation);
    }

    private bool IsCurrentLocked(long generation) =>
        _active && !_disposed && generation == _generation;
}
