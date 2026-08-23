using Scada.Core.Tags;
using Scada.Runtime.Tags;

namespace Scada.App.Tests;

internal sealed class TestTagCache : ITagCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, TagValue> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TestSubscription> _subscriptions = [];

    public int TryGetCount { get; private set; }

    public Action? SubscribeHook { get; set; }

    public int ActiveSubscriptionCount
    {
        get
        {
            lock (_sync)
            {
                return _subscriptions.Count(subscription => !subscription.IsDisposed);
            }
        }
    }

    public int TotalSubscriptionCount
    {
        get
        {
            lock (_sync)
            {
                return _subscriptions.Count;
            }
        }
    }

    public int DisposedSubscriptionCount
    {
        get
        {
            lock (_sync)
            {
                return _subscriptions.Count(subscription => subscription.IsDisposed);
            }
        }
    }

    public bool TryGet(string tagId, out TagValue? value)
    {
        lock (_sync)
        {
            TryGetCount++;
            if (_values.TryGetValue(tagId, out var current))
            {
                value = current;
                return true;
            }

            value = null;
            return false;
        }
    }

    public IDisposable Subscribe(string tagId, Action<TagValue> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        ArgumentNullException.ThrowIfNull(callback);

        var subscription = new TestSubscription(tagId, callback);
        lock (_sync)
        {
            _subscriptions.Add(subscription);
        }

        SubscribeHook?.Invoke();

        return subscription;
    }

    public void Seed(TagValue value)
    {
        lock (_sync)
        {
            _values[value.TagId] = value;
        }
    }

    public void Publish(TagValue value)
    {
        Action<TagValue>[] callbacks;
        lock (_sync)
        {
            _values[value.TagId] = value;
            callbacks = _subscriptions
                .Where(subscription => !subscription.IsDisposed
                    && string.Equals(subscription.TagId, value.TagId, StringComparison.OrdinalIgnoreCase))
                .Select(subscription => subscription.Callback)
                .ToArray();
        }

        foreach (var callback in callbacks)
        {
            callback(value);
        }
    }

    public void InvokeSubscription(int index, TagValue value)
    {
        TestSubscription subscription;
        lock (_sync)
        {
            subscription = _subscriptions[index];
        }

        subscription.Callback(value);
    }

    private sealed class TestSubscription(string tagId, Action<TagValue> callback) : IDisposable
    {
        private int _disposed;

        public string TagId { get; } = tagId;

        public Action<TagValue> Callback { get; } = callback;

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }
}
