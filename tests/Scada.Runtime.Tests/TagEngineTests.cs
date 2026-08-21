using Scada.Core.Drivers;
using Scada.Core.Tags;
using Scada.Runtime.Engine;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class TagEngineTests
{
    [Fact]
    public void DisconnectWithoutSuccessfulValueUsesTransitionTimestamp()
    {
        var cache = new TagCache();
        var engine = new TagEngine(cache);
        var tag = new TagDefinition { Id = "T1", DeviceId = "PLC-1", Address = "A1" };
        var transition = DateTimeOffset.Parse("2026-01-01T00:00:10Z");

        engine.MarkDeviceDisconnected([tag], transition);

        Assert.True(cache.TryGet("T1", out var value));
        Assert.Null(value!.Value);
        Assert.Equal(TagQuality.Disconnected, value.Quality);
        Assert.Equal(transition, value.Timestamp);
    }

    [Fact]
    public void DisconnectAfterSuccessfulValuePreservesValueAndPlcTimestamp()
    {
        var cache = new TagCache();
        var engine = new TagEngine(cache);
        var tag = new TagDefinition { Id = "T1", DeviceId = "PLC-1", Address = "A1" };
        var plcTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:05Z");
        var transition = DateTimeOffset.Parse("2026-01-01T00:00:10Z");

        engine.Apply([new DriverReadResult("T1", 42, TagQuality.Good, plcTimestamp)]);
        engine.MarkDeviceDisconnected([tag], transition);

        Assert.True(cache.TryGet("T1", out var value));
        Assert.Equal(42, value!.Value);
        Assert.Equal(TagQuality.Disconnected, value.Quality);
        Assert.Equal(plcTimestamp, value.Timestamp);
        Assert.NotEqual(transition, value.Timestamp);
    }

    [Fact]
    public void RepeatedDisconnectPreservesEachTimestampContract()
    {
        var cache = new TagCache();
        var engine = new TagEngine(cache);
        var tag = new TagDefinition { Id = "T1", DeviceId = "PLC-1", Address = "A1" };
        var firstTransition = DateTimeOffset.Parse("2026-01-01T00:00:10Z");
        var secondTransition = DateTimeOffset.Parse("2026-01-01T00:00:20Z");

        engine.MarkDeviceDisconnected([tag], firstTransition);
        engine.MarkDeviceDisconnected([tag], secondTransition);

        Assert.True(cache.TryGet("T1", out var withoutValue));
        Assert.Equal(secondTransition, withoutValue!.Timestamp);

        var plcTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:25Z");
        engine.Apply([new DriverReadResult("T1", 7, TagQuality.Good, plcTimestamp)]);
        engine.MarkDeviceDisconnected([tag], DateTimeOffset.Parse("2026-01-01T00:00:30Z"));
        engine.MarkDeviceDisconnected([tag], DateTimeOffset.Parse("2026-01-01T00:00:40Z"));

        Assert.True(cache.TryGet("T1", out var withValue));
        Assert.Equal(7, withValue!.Value);
        Assert.Equal(plcTimestamp, withValue.Timestamp);
    }
}
