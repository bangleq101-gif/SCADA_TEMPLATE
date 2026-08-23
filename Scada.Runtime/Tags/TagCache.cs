using System.Diagnostics;
using Scada.Core.Tags;

namespace Scada.Runtime.Tags;

public sealed class TagCache : ITagCache
{
    private readonly bool _metricsEnabled;
    private readonly object _sync = new();
    private readonly Dictionary<string, TagValue> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<TagValue>>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _sequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tagsWithGoodValue = new(StringComparer.OrdinalIgnoreCase);
    private long _updates;
    private long _callbackInvocations;
    private long _subscriberExceptions;

    public TagCache(bool metricsEnabled = false)
    {
        _metricsEnabled = metricsEnabled;
    }

    public TagCacheRuntimeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new TagCacheRuntimeSnapshot(
                    Interlocked.Read(ref _updates),
                    Interlocked.Read(ref _callbackInvocations),
                    Interlocked.Read(ref _subscriberExceptions),
                    _values.Count,
                    _subscriptions.Values.Sum(callbacks => callbacks.Count));
            }
        }
    }

    public bool TryGet(string tagId, out TagValue? value)
    {
        lock (_sync)
        {
            return _values.TryGetValue(tagId, out value);
        }
    }

    public IDisposable Subscribe(string tagId, Action<TagValue> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        ArgumentNullException.ThrowIfNull(callback);

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(tagId, out var callbacks))
            {
                callbacks = [];
                _subscriptions[tagId] = callbacks;
            }

            callbacks.Add(callback);
        }

        return new Subscription(() => RemoveSubscription(tagId, callback));
    }

    public TagValue Upsert(TagUpdate update)
    {
        Action<TagValue>[] callbacks;
        TagValue value;

        lock (_sync)
        {
            var sequence = _sequences.TryGetValue(update.TagId, out var current) ? current + 1 : 1;
            _sequences[update.TagId] = sequence;

            var valueToPublish = update.Value;
            var timestampToPublish = update.Timestamp;
            if (update.Quality == TagQuality.Good)
            {
                _tagsWithGoodValue.Add(update.TagId);
            }
            else if (_tagsWithGoodValue.Contains(update.TagId)
                && _values.TryGetValue(update.TagId, out var lastValue))
            {
                valueToPublish = lastValue.Value;
                timestampToPublish = lastValue.Timestamp;
            }
            else
            {
                valueToPublish = null;
            }

            value = new TagValue(
                update.TagId,
                valueToPublish,
                update.Quality,
                timestampToPublish,
                sequence);
            _values[update.TagId] = value;
            if (_metricsEnabled) Interlocked.Increment(ref _updates);
            callbacks = _subscriptions.TryGetValue(update.TagId, out var subscribers)
                ? subscribers.ToArray()
                : [];
        }

        foreach (var callback in callbacks)
        {
            if (_metricsEnabled) Interlocked.Increment(ref _callbackInvocations);
            try
            {
                callback(value);
            }
            catch (Exception exception)
            {
                if (_metricsEnabled) Interlocked.Increment(ref _subscriberExceptions);
                Debug.WriteLine($"TagCache subscriber failed for '{value.TagId}': {exception}");
            }
        }

        return value;
    }

    private void RemoveSubscription(string tagId, Action<TagValue> callback)
    {
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(tagId, out var callbacks))
            {
                return;
            }

            callbacks.Remove(callback);
            if (callbacks.Count == 0)
            {
                _subscriptions.Remove(tagId);
            }
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
}
