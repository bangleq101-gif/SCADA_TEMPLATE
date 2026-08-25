using Microsoft.Extensions.Logging;
using Scada.Runtime.Health;

namespace Scada.App.Services;

public sealed class RuntimeHealthPresentationService : IDisposable
{
    private readonly RuntimeHealthService _runtimeHealth;
    private readonly ILogger<RuntimeHealthPresentationService> _logger;
    private readonly object _sync = new();
    private readonly List<Action<RuntimeHealthSnapshot>> _subscribers = [];
    private IDisposable? _runtimeSubscription;
    private RuntimeHealthSnapshot _snapshot;
    private bool _disposed;

    public RuntimeHealthPresentationService(
        RuntimeHealthService runtimeHealth,
        ILogger<RuntimeHealthPresentationService> logger)
    {
        _runtimeHealth = runtimeHealth ?? throw new ArgumentNullException(nameof(runtimeHealth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _snapshot = runtimeHealth.Snapshot;
        _runtimeSubscription = runtimeHealth.Subscribe(Receive);
    }

    public RuntimeHealthSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public IDisposable Subscribe(Action<RuntimeHealthSnapshot> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_sync)
        {
            if (_disposed)
            {
                return EmptySubscription.Instance;
            }

            _subscribers.Add(callback);
        }

        Notify(callback, Snapshot);
        return new Subscription(() =>
        {
            lock (_sync)
            {
                _subscribers.Remove(callback);
            }
        });
    }

    public void Dispose()
    {
        IDisposable? runtimeSubscription;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscribers.Clear();
            runtimeSubscription = _runtimeSubscription;
            _runtimeSubscription = null;
        }

        runtimeSubscription?.Dispose();
    }

    private void Receive(RuntimeHealthSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
        Action<RuntimeHealthSnapshot>[] subscribers;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            subscribers = _subscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            Notify(subscriber, snapshot);
        }
    }

    private void Notify(Action<RuntimeHealthSnapshot> subscriber, RuntimeHealthSnapshot snapshot)
    {
        try
        {
            subscriber(snapshot);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Runtime health presentation subscriber failed.");
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
            }
        }
    }

    private sealed class EmptySubscription : IDisposable
    {
        public static EmptySubscription Instance { get; } = new();
        public void Dispose() { }
    }
}
