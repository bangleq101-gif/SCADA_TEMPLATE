using Microsoft.Extensions.Logging.Abstractions;
using Scada.App.ViewModels;
using Scada.Core.Alarms;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Scada.Runtime.Alarms;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class AlarmSnapshotCoalescingTests
{
    [Fact]
    public async Task MonitoringLatestSnapshotWinsWithOnePendingDispatcherItem()
    {
        var options = CreateOptions();
        var cache = new TagCache();
        await using var runtime = new AlarmRuntimeService(
            options, cache, null, NullLogger<AlarmRuntimeService>.Instance, TimeProvider.System);
        await runtime.StartAsync(CancellationToken.None);
        var dispatcher = new QueuedAlarmDispatcher();
        using var monitoring = new AlarmMonitoringViewModel(runtime, null, TimeProvider.System, dispatcher);
        monitoring.Activate();
        dispatcher.DrainAll();

        cache.Upsert(new TagUpdate("T1", 90d, TagQuality.Good, DateTimeOffset.UtcNow));
        cache.Upsert(new TagUpdate("T1", 0d, TagQuality.Good, DateTimeOffset.UtcNow));
        cache.Upsert(new TagUpdate("T1", 90d, TagQuality.Good, DateTimeOffset.UtcNow));

        Assert.Equal(1, dispatcher.PendingCount);
        Assert.Equal(1, monitoring.PendingDispatcherUpdateCount);
        dispatcher.DrainAll();

        var row = Assert.Single(monitoring.Rows);
        Assert.Equal(AlarmLifecycleState.ActiveUnacknowledged, row.State);
        Assert.Equal(0, monitoring.PendingDispatcherUpdateCount);
    }

    [Fact]
    public async Task OperationLatestSnapshotWinsAndOldGenerationCannotApply()
    {
        var options = CreateOptions();
        var cache = new TagCache();
        await using var runtime = new AlarmRuntimeService(
            options, cache, null, NullLogger<AlarmRuntimeService>.Instance, TimeProvider.System);
        await runtime.StartAsync(CancellationToken.None);
        var dispatcher = new QueuedAlarmDispatcher();
        using var operation = new OperationViewModel(options, runtime, dispatcher);

        operation.Activate();
        dispatcher.DrainAll();
        cache.Upsert(new TagUpdate("T1", 90d, TagQuality.Good, DateTimeOffset.UtcNow));
        operation.Deactivate();
        operation.Activate();

        Assert.True(dispatcher.PendingCount >= 2); // stale old-generation item plus the new activation item
        dispatcher.DrainOne();
        Assert.Equal(0, operation.ActiveAlarmCount);
        dispatcher.DrainAll();
        Assert.Equal(1, operation.ActiveAlarmCount);
        Assert.Equal(0, operation.PendingAlarmDispatcherUpdateCount);

        operation.Deactivate();
        cache.Upsert(new TagUpdate("T1", 0d, TagQuality.Good, DateTimeOffset.UtcNow));
        Assert.Equal(0, operation.OwnedAlarmSubscriptionCount);
        dispatcher.DrainAll();
        Assert.Equal(1, operation.ActiveAlarmCount);
    }

    [Fact]
    public async Task MonitoringDeactivationInvalidatesQueuedSnapshotAndOwnsNoSubscription()
    {
        var options = CreateOptions();
        var cache = new TagCache();
        await using var runtime = new AlarmRuntimeService(
            options, cache, null, NullLogger<AlarmRuntimeService>.Instance, TimeProvider.System);
        await runtime.StartAsync(CancellationToken.None);
        var dispatcher = new QueuedAlarmDispatcher();
        using var monitoring = new AlarmMonitoringViewModel(runtime, null, TimeProvider.System, dispatcher);

        monitoring.Activate();
        dispatcher.DrainAll();
        cache.Upsert(new TagUpdate("T1", 90d, TagQuality.Good, DateTimeOffset.UtcNow));
        monitoring.Deactivate();

        Assert.Equal(0, monitoring.OwnedSubscriptionCount);
        dispatcher.DrainAll();
        Assert.Empty(monitoring.Rows);
    }

    private static RuntimeOptions CreateOptions() => new()
    {
        Tags = [new TagDefinition { Id = "T1", Name = "T1", DeviceId = "SIM", Address = "T1", DataType = TagDataType.Double }],
        Alarms = new AlarmOptions
        {
            Enabled = true,
            PersistenceEnabled = false,
            Definitions = [new AlarmDefinition
            {
                Id = "A1", Name = "High", Message = "High", TagId = "T1", RuleType = AlarmRuleType.High,
                Threshold = 80, AcknowledgementRequired = true
            }]
        }
    };

    private sealed class QueuedAlarmDispatcher : IAlarmSnapshotDispatcher
    {
        private readonly Queue<Action> _actions = [];

        public int PendingCount => _actions.Count;

        public void Post(Action action) => _actions.Enqueue(action);

        public void DrainOne() => _actions.Dequeue()();

        public void DrainAll()
        {
            while (_actions.Count > 0) DrainOne();
        }
    }
}
