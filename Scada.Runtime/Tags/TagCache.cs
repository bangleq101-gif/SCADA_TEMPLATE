using System.Diagnostics;
using Scada.Core.Tags;

namespace Scada.Runtime.Tags;

public sealed class TagCache : ITagCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, TagValue> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<TagValue>>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _sequences = new(StringComparer.OrdinalIgnoreCase);

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
            value = new TagValue(update.TagId, update.Value, update.Quality, update.Timestamp, sequence);
            _values[update.TagId] = value;
            callbacks = _subscriptions.TryGetValue(update.TagId, out var subscribers)
                ? subscribers.ToArray()
                : [];
        }

        foreach (var callback in callbacks)
        {
            try
            {
                callback(value);
            }
            catch (Exception exception)
            {
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
