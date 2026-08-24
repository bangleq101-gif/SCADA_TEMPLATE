using Scada.Core.Tags;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class TagCacheTests
{
    [Fact]
    public void UpsertPublishesAndIncrementsSequence()
    {
        var cache = new TagCache(metricsEnabled: true);
        var received = new List<TagValue>();
        using var subscription = cache.Subscribe("T1", received.Add);

        cache.Upsert(new TagUpdate("T1", 1, TagQuality.Good, DateTimeOffset.UtcNow));
        cache.Upsert(new TagUpdate("T1", 2, TagQuality.Good, DateTimeOffset.UtcNow));

        Assert.Equal(2, received.Count);
        Assert.Equal(2, received[^1].Sequence);
        Assert.True(cache.TryGet("T1", out var current));
        Assert.Equal(2, current!.Value);
    }

    [Fact]
    public void DisposeStopsSubscription()
    {
        var cache = new TagCache();
        var count = 0;
        var subscription = cache.Subscribe("T1", _ => count++);
        subscription.Dispose();

        cache.Upsert(new TagUpdate("T1", true, TagQuality.Good, DateTimeOffset.UtcNow));

        Assert.Equal(0, count);
    }

    [Fact]
    public void SubscriberExceptionDoesNotBlockOtherSubscribersOrUpsert()
    {
        var cache = new TagCache(metricsEnabled: true);
        var received = 0;
        using var throwingSubscription = cache.Subscribe("T1", _ => throw new InvalidOperationException("subscriber failure"));
        using var healthySubscription = cache.Subscribe("T1", _ => received++);

        var value = cache.Upsert(new TagUpdate("T1", 42, TagQuality.Good, DateTimeOffset.UtcNow));

        Assert.Equal(1, received);
        Assert.Equal(1, value.Sequence);
        Assert.True(cache.TryGet("T1", out var current));
        Assert.Equal(42, current!.Value);
        Assert.Equal(1, cache.Snapshot.SubscriberExceptions);
        Assert.Equal(2, cache.Snapshot.CallbackInvocations);
        Assert.Equal(1, cache.Snapshot.Updates);
    }
}
