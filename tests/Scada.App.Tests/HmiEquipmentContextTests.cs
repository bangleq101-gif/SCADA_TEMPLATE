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

    [Theory]
    [InlineData(HmiEquipmentKind.Motor, HmiTagRole.Run, true)]
    [InlineData(HmiEquipmentKind.Pump, HmiTagRole.Run, true)]
    [InlineData(HmiEquipmentKind.Valve, HmiTagRole.Position, false)]
    [InlineData(HmiEquipmentKind.Tank, HmiTagRole.Level, 50d)]
    [InlineData(HmiEquipmentKind.Pipe, HmiTagRole.Flow, true)]
    [InlineData(HmiEquipmentKind.Conveyor, HmiTagRole.Run, false)]
    [InlineData(HmiEquipmentKind.Indicator, HmiTagRole.Value, true)]
    public void EveryEquipmentKindHasDeterministicRequiredRoleContract(HmiEquipmentKind kind, HmiTagRole role, object value)
    {
        var tags = new Dictionary<HmiTagRole, string> { [role] = "x" };
        var values = new Dictionary<HmiTagRole, TagValue> { [role] = Value("x", value) };
        Assert.NotEqual(HmiVisualState.Unknown, HmiStateEvaluator.Evaluate(kind, tags, values));
    }

    [Fact]
    public void IndicatorSupportsBothDiscreteAndAnalogValues()
    {
        var tags = new Dictionary<HmiTagRole, string> { [HmiTagRole.Value] = "i" };
        Assert.Equal(HmiVisualState.Stopped, HmiStateEvaluator.Evaluate(HmiEquipmentKind.Indicator, tags, new Dictionary<HmiTagRole, TagValue> { [HmiTagRole.Value] = Value("i", false) }));
        Assert.Equal(HmiVisualState.Running, HmiStateEvaluator.Evaluate(HmiEquipmentKind.Indicator, tags, new Dictionary<HmiTagRole, TagValue> { [HmiTagRole.Value] = Value("i", 12.5d) }));
    }

    [Fact]
    public void FiveHundredContextsReleaseAllOwnedSubscriptions()
    {
        var cache = new TestTagCache();
        var contexts = Enumerable.Range(0, 500).Select(index => new HmiEquipmentContext(cache, HmiEquipmentKind.Motor, $"M{index}", $"M{index}", new Dictionary<HmiTagRole, string> { [HmiTagRole.Run] = $"R{index}", [HmiTagRole.Warning] = $"R{index}", [HmiTagRole.Fault] = $"F{index}", [HmiTagRole.Ready] = $"D{index}" })).ToArray();
        foreach (var context in contexts) context.Activate();
        Assert.Equal(1_500, cache.ActiveSubscriptionCount);
        foreach (var context in contexts) context.Deactivate();
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Theory]
    [InlineData(-10d, 0d)]
    [InlineData(25d, 0.25d)]
    [InlineData(150d, 1d)]
    public void TankLevelProducesAClampedFillFraction(double level, double expectedFraction)
    {
        var cache = new TestTagCache();
        var context = new HmiEquipmentContext(cache, HmiEquipmentKind.Tank, "T1", "Tank 1", new Dictionary<HmiTagRole, string> { [HmiTagRole.Level] = "level" });

        context.Activate();
        cache.Publish(Value("level", level));

        Assert.Equal(expectedFraction, context.TankFillFraction, 3);
    }

    [Fact]
    public void TankFillFractionTracksRuntimeLevelUpdates()
    {
        var cache = new TestTagCache();
        var context = new HmiEquipmentContext(cache, HmiEquipmentKind.Tank, "T1", "Tank 1", new Dictionary<HmiTagRole, string> { [HmiTagRole.Level] = "level" });

        context.Activate();
        cache.Publish(Value("level", 20d));
        Assert.Equal(0.2d, context.TankFillFraction, 3);

        cache.Publish(new TagValue("level", 80d, TagQuality.Good, DateTimeOffset.UnixEpoch, 2));
        Assert.Equal(0.8d, context.TankFillFraction, 3);
    }

    [Theory]
    [InlineData(HmiEquipmentKind.Motor, FaceplateTemplateSelector.MotorTemplateKey)]
    [InlineData(HmiEquipmentKind.Pump, FaceplateTemplateSelector.PumpTemplateKey)]
    [InlineData(HmiEquipmentKind.Valve, FaceplateTemplateSelector.ValveTemplateKey)]
    [InlineData(HmiEquipmentKind.Tank, FaceplateTemplateSelector.AnalogTemplateKey)]
    [InlineData(HmiEquipmentKind.Indicator, FaceplateTemplateSelector.AnalogTemplateKey)]
    [InlineData(HmiEquipmentKind.Pipe, FaceplateTemplateSelector.AnalogTemplateKey)]
    [InlineData(HmiEquipmentKind.Conveyor, FaceplateTemplateSelector.AnalogTemplateKey)]
    public void FaceplateTemplateSelectorUsesTheEquipmentKindContract(HmiEquipmentKind kind, string expectedKey)
    {
        Assert.Equal(expectedKey, FaceplateTemplateSelector.GetTemplateKey(kind));
    }

    [Fact]
    public async Task ConcurrentLifecycleCallsLeaveNoOwnedSubscriptions()
    {
        var cache = new TestTagCache();
        var context = Context(cache, "M1");

        await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() =>
        {
            if (index % 3 == 0) context.Activate();
            else if (index % 3 == 1) context.Deactivate();
            else context.Dispose();
        })));

        context.Deactivate();
        context.Dispose();
        Assert.Equal(0, cache.ActiveSubscriptionCount);
    }

    [Fact]
    public async Task DeactivateDuringSubscriptionAcquisitionDisposesTheUnownedSubscription()
    {
        var cache = new TestTagCache();
        using var subscribed = new ManualResetEventSlim();
        using var releaseSubscribe = new ManualResetEventSlim();
        cache.SubscribeHook = () => { subscribed.Set(); releaseSubscribe.Wait(); };
        var context = Context(cache, "M1");

        var activation = Task.Run(context.Activate);
        Assert.True(subscribed.Wait(TimeSpan.FromSeconds(5)));
        context.Deactivate();
        context.Dispose();
        releaseSubscribe.Set();
        await activation;

        Assert.Equal(0, cache.ActiveSubscriptionCount);
        Assert.Equal(1, cache.DisposedSubscriptionCount);
    }

    private static HmiEquipmentContext Context(TestTagCache cache, string id) => new(cache, HmiEquipmentKind.Motor, id, id, new Dictionary<HmiTagRole, string> { [HmiTagRole.Run] = id });
    private static TagValue Value(string id, object value, TagQuality quality = TagQuality.Good) => new(id, value, quality, DateTimeOffset.UnixEpoch, 1);
    private sealed class QueuedDispatcher : IHmiDispatcher { private readonly Queue<Action> _actions = []; public void Post(Action action) => _actions.Enqueue(action); public void Flush() { while (_actions.TryDequeue(out var action)) action(); } }
}
