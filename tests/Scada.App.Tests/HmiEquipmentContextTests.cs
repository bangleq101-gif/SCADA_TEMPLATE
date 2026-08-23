using Scada.App.Hmi;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class HmiEquipmentContextTests
{
    [Fact]
    public void RequiredRoleAndValueShapeAreDeterministicallyUnknown()
    {
        var tags = new Dictionary<HmiTagRole, string> { [HmiTagRole.Run] = "run" };
        Assert.Equal(HmiVisualState.Unknown, HmiStateEvaluator.Evaluate(HmiEquipmentKind.Motor, tags, new Dictionary<HmiTagRole, TagValue>()));
        var invalid = new Dictionary<HmiTagRole, TagValue> { [HmiTagRole.Run] = Value("run", 1) };
        Assert.Equal(HmiVisualState.Unknown, HmiStateEvaluator.Evaluate(HmiEquipmentKind.Motor, tags, invalid));
    }

    [Fact]
    public void QualityFaultWarningAndRunUseDefinedPrecedence()
    {
        var tags = new Dictionary<HmiTagRole, string> { [HmiTagRole.Run] = "run", [HmiTagRole.Fault] = "fault", [HmiTagRole.Warning] = "warning" };
        var values = new Dictionary<HmiTagRole, TagValue> { [HmiTagRole.Run] = Value("run", true), [HmiTagRole.Fault] = Value("fault", true), [HmiTagRole.Warning] = Value("warning", true) };
        Assert.Equal(HmiVisualState.Fault, HmiStateEvaluator.Evaluate(HmiEquipmentKind.Motor, tags, values));
        values[HmiTagRole.Run] = Value("run", true, TagQuality.Disconnected);
        Assert.Equal(HmiVisualState.BadQuality, HmiStateEvaluator.Evaluate(HmiEquipmentKind.Motor, tags, values));
    }

    [Fact]
    public void ContextDeduplicatesTagSubscriptionsAndRejectsStaleQueuedUpdate()
    {
        var cache = new TestTagCache(); var dispatcher = new QueuedDispatcher();
        var context = new HmiEquipmentContext(cache, HmiEquipmentKind.Motor, "M1", "Motor 1", new Dictionary<HmiTagRole, string> { [HmiTagRole.Run] = "state", [HmiTagRole.Warning] = "state" }, dispatcher);
        context.Activate();
        Assert.Equal(1, cache.ActiveSubscriptionCount);
        cache.Publish(Value("state", true));
        context.Deactivate(); dispatcher.Flush();
        Assert.Equal(HmiVisualState.Unknown, context.State);
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void FaceplateHostBorrowsAndReplacesContextWithoutDisposal()
    {
        var cache = new TestTagCache(); var one = Context(cache, "M1"); var two = Context(cache, "M2"); one.Activate();
        var host = new FaceplateHostViewModel(); host.Open(one); host.Open(two); host.Close();
        Assert.Equal(1, cache.ActiveSubscriptionCount);
        one.Deactivate(); Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    private static HmiEquipmentContext Context(TestTagCache cache, string id) => new(cache, HmiEquipmentKind.Motor, id, id, new Dictionary<HmiTagRole, string> { [HmiTagRole.Run] = id });
    private static TagValue Value(string id, object value, TagQuality quality = TagQuality.Good) => new(id, value, quality, DateTimeOffset.UnixEpoch, 1);
    private sealed class QueuedDispatcher : IHmiDispatcher { private readonly Queue<Action> _actions = []; public void Post(Action action) => _actions.Enqueue(action); public void Flush() { while (_actions.TryDequeue(out var action)) action(); } }
}
