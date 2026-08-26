using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class MonitoringBoundedSubscriptionTests
{
    [Fact]
    public void TenThousandTagsUseOnlyTheVisiblePageOfSubscriptions()
    {
        var cache = new TestTagCache();
        var dispatcher = new QueuedMonitoringDispatcher();
        using var monitoring = new MonitoringViewModel(cache, CreateOptions(10_000), dispatcher);

        monitoring.Activate();

        Assert.Equal(10_000, monitoring.TotalMatchingTags);
        Assert.Equal(MonitoringViewModel.DefaultPageSize, monitoring.Rows.Count);
        Assert.Equal(MonitoringViewModel.DefaultPageSize, cache.ActiveSubscriptionCount);
        Assert.Equal("T0000", monitoring.Rows[0].TagId);

        monitoring.MoveToNextPage();

        Assert.Equal("T0250", monitoring.Rows[0].TagId);
        Assert.Equal(MonitoringViewModel.DefaultPageSize, cache.ActiveSubscriptionCount);
        Assert.Equal(MonitoringViewModel.DefaultPageSize * 2, cache.TotalSubscriptionCount);
        Assert.DoesNotContain("T0000", cache.ActiveTagIds, StringComparer.OrdinalIgnoreCase);

        monitoring.Deactivate();

        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void MetadataSearchAndDeviceFilterRebuildOnlyTheBoundedVisibleSet()
    {
        var cache = new TestTagCache();
        using var monitoring = new MonitoringViewModel(cache, CreateOptions(600), new QueuedMonitoringDispatcher());
        monitoring.Activate();

        monitoring.SelectedDeviceId = "SIM02";

        Assert.Equal(300, monitoring.TotalMatchingTags);
        Assert.Equal(250, monitoring.Rows.Count);
        Assert.Equal(250, cache.ActiveSubscriptionCount);
        Assert.All(monitoring.Rows, row => Assert.Equal("SIM02", row.DeviceId));

        monitoring.SearchText = "T050";

        Assert.Equal(5, monitoring.TotalMatchingTags);
        Assert.Equal(5, monitoring.Rows.Count);
        Assert.Equal(5, cache.ActiveSubscriptionCount);
        Assert.All(monitoring.Rows, row => Assert.Contains("T050", row.TagId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConcurrentTagUpdatesAreCoalescedToTheLatestValuePerVisibleTag()
    {
        var cache = new TestTagCache();
        var dispatcher = new QueuedMonitoringDispatcher();
        using var monitoring = new MonitoringViewModel(cache, CreateOptions(1), dispatcher);
        monitoring.Activate();

        cache.Publish(Value("T0000", 1, 1));
        cache.Publish(Value("T0000", 2, 2));
        cache.Publish(Value("T0000", 3, 3));

        Assert.Equal(1, dispatcher.PendingCount);
        Assert.Null(Assert.Single(monitoring.Rows).Value);

        dispatcher.DrainAll();

        var row = Assert.Single(monitoring.Rows);
        Assert.Equal(3, row.Value);
        Assert.Equal(3, row.Sequence);
    }

    [Fact]
    public void OlderConcurrentCallbackCannotReplaceNewerPendingValue()
    {
        var cache = new TestTagCache();
        var dispatcher = new QueuedMonitoringDispatcher();
        using var monitoring = new MonitoringViewModel(cache, CreateOptions(1), dispatcher);
        monitoring.Activate();

        cache.Publish(Value("T0000", 20, 20));
        cache.InvokeSubscription(0, Value("T0000", 10, 10));
        dispatcher.DrainAll();

        var row = Assert.Single(monitoring.Rows);
        Assert.Equal(20, row.Value);
        Assert.Equal(20, row.Sequence);
    }

    [Fact]
    public void StaleQueuedDispatcherCallbackCannotUpdateAfterDeactivation()
    {
        var cache = new TestTagCache();
        var dispatcher = new QueuedMonitoringDispatcher();
        using var monitoring = new MonitoringViewModel(cache, CreateOptions(1), dispatcher);
        monitoring.Activate();

        cache.Publish(Value("T0000", 7, 1));
        monitoring.Deactivate();
        dispatcher.DrainAll();

        Assert.Null(Assert.Single(monitoring.Rows).Value);
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void StaleQueuedDispatcherCallbackCannotClearNewerPageUpdates()
    {
        var cache = new TestTagCache();
        var dispatcher = new QueuedMonitoringDispatcher();
        using var monitoring = new MonitoringViewModel(cache, CreateOptions(251), dispatcher);
        monitoring.Activate();

        cache.Publish(Value("T0000", 1, 1));
        monitoring.MoveToNextPage();
        cache.Publish(Value("T0250", 2, 2));

        dispatcher.DrainAll();

        var currentRow = Assert.Single(monitoring.Rows);
        Assert.Equal("T0250", currentRow.TagId);
        Assert.Equal(2, currentRow.Value);
        Assert.Equal(2, currentRow.Sequence);
    }

    [Fact]
    public void SubscribeAcquisitionInterruptedByDeactivateDisposesTheNewSubscription()
    {
        var cache = new TestTagCache();
        var dispatcher = new QueuedMonitoringDispatcher();
        MonitoringViewModel? monitoring = null;
        cache.SubscribeHook = () => monitoring!.Deactivate();
        monitoring = new MonitoringViewModel(cache, CreateOptions(1), dispatcher);

        monitoring.Activate();

        Assert.Equal(0, cache.ActiveSubscriptionCount);
        Assert.Equal(1, cache.DisposedSubscriptionCount);
    }

    [Fact]
    public void SubscribeAcquisitionInterruptedByDisposeDisposesTheNewSubscription()
    {
        var cache = new TestTagCache();
        var dispatcher = new QueuedMonitoringDispatcher();
        MonitoringViewModel? monitoring = null;
        cache.SubscribeHook = () => monitoring!.Dispose();
        monitoring = new MonitoringViewModel(cache, CreateOptions(1), dispatcher);

        monitoring.Activate();

        Assert.Equal(0, cache.ActiveSubscriptionCount);
        Assert.Equal(1, cache.DisposedSubscriptionCount);
    }

    [Fact]
    public void DuplicateVisibleTagIdsAcquireOnlyOneOwnedSubscription()
    {
        var cache = new TestTagCache();
        var options = CreateOptions(1);
        options.Tags.Add(new TagDefinition
        {
            Id = "T0000",
            Name = "Duplicate",
            DeviceId = "SIM01",
            Address = "A-duplicate"
        });
        using var monitoring = new MonitoringViewModel(cache, options, new QueuedMonitoringDispatcher());

        monitoring.Activate();

        Assert.Equal(1, cache.ActiveSubscriptionCount);
        Assert.Equal(1, cache.TotalSubscriptionCount);
    }

    [Fact]
    public void SubscribeBeforeSeedDeliversTheCurrentCacheValue()
    {
        var cache = new TestTagCache();
        var dispatcher = new QueuedMonitoringDispatcher();
        using var monitoring = new MonitoringViewModel(cache, CreateOptions(1), dispatcher);
        cache.SubscribeHook = () => cache.Publish(Value("T0000", 99, 9));

        monitoring.Activate();
        dispatcher.DrainAll();

        var row = Assert.Single(monitoring.Rows);
        Assert.Equal(99, row.Value);
        Assert.Equal(9, row.Sequence);
    }

    private static RuntimeOptions CreateOptions(int count) => new()
    {
        Tags = Enumerable.Range(0, count)
            .Select(index => new TagDefinition
            {
                Id = $"T{index:D4}",
                Name = $"Tag {index:D4}",
                DeviceId = index % 2 == 0 ? "SIM01" : "SIM02",
                Address = $"A{index}",
                DataType = TagDataType.Double
            })
            .ToList()
    };

    private static TagValue Value(string tagId, object value, long sequence) =>
        new(tagId, value, TagQuality.Good, DateTimeOffset.UtcNow, sequence);

    private sealed class QueuedMonitoringDispatcher : IMonitoringDispatcher
    {
        private readonly object _sync = new();
        private readonly List<Action> _callbacks = [];

        public int PendingCount
        {
            get
            {
                lock (_sync)
                {
                    return _callbacks.Count;
                }
            }
        }

        public bool CheckAccess() => false;

        public void Enqueue(Action callback)
        {
            lock (_sync)
            {
                _callbacks.Add(callback);
            }
        }

        public void DrainAll()
        {
            while (true)
            {
                Action[] callbacks;
                lock (_sync)
                {
                    if (_callbacks.Count == 0)
                    {
                        return;
                    }

                    callbacks = _callbacks.ToArray();
                    _callbacks.Clear();
                }

                foreach (var callback in callbacks)
                {
                    callback();
                }
            }
        }
    }
}
