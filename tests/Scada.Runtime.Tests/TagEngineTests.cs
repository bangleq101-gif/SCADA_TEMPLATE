using Scada.Core.Drivers;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Microsoft.Extensions.Logging.Abstractions;
using Scada.Runtime.Engine;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class TagEngineTests
{
    [Fact]
    public void GoodDriverValueIsTransformedOnceBeforeItReachesTagCache()
    {
        var cache = new TagCache();
        var options = new RuntimeOptions
        {
            Tags =
            [
                new TagDefinition
                {
                    Id = "LEVEL",
                    DeviceId = "PLC-1",
                    Address = "DB1.DBD0",
                    SourceDataType = TagDataType.Int32,
                    DataType = TagDataType.Double,
                    Scale = 0.1d,
                    Offset = -20d
                }
            ]
        };
        var engine = new TagEngine(cache, options, NullLogger<TagEngine>.Instance);
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:05Z");

        engine.Apply([new DriverReadResult("LEVEL", 1_234, TagQuality.Good, timestamp)]);

        var value = Assert.IsType<TagValue>(cache.TryGet("LEVEL", out var cached) ? cached : null);
        Assert.Equal(103.4d, Assert.IsType<double>(value.Value), precision: 10);
        Assert.Equal(TagQuality.Good, value.Quality);
        Assert.Equal(timestamp, value.Timestamp);
    }

    [Fact]
    public void InvalidGoodEngineeringTransformBecomesBadWithoutReplacingLastCanonicalGoodValue()
    {
        var cache = new TagCache();
        var options = new RuntimeOptions
        {
            Tags =
            [
                new TagDefinition
                {
                    Id = "COUNT",
                    DeviceId = "PLC-1",
                    Address = "DB1.DBD4",
                    SourceDataType = TagDataType.Int32,
                    DataType = TagDataType.Double,
                    Scale = 0.5d
                }
            ]
        };
        var engine = new TagEngine(cache, options, NullLogger<TagEngine>.Instance);
        var goodTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:05Z");
        var invalidTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:10Z");

        engine.Apply([new DriverReadResult("COUNT", 10, TagQuality.Good, goodTimestamp)]);
        engine.Apply([new DriverReadResult("COUNT", 10.5d, TagQuality.Good, invalidTimestamp)]);

        Assert.True(cache.TryGet("COUNT", out var value));
        Assert.Equal(5d, Assert.IsType<double>(value!.Value));
        Assert.Equal(TagQuality.Bad, value.Quality);
        Assert.Equal(goodTimestamp, value.Timestamp);
    }

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
    public void GoodBadDisconnectPreservesLastGoodValueAndTimestamp()
    {
        var cache = new TagCache();
        var engine = new TagEngine(cache);
        var tag = new TagDefinition { Id = "T1", DeviceId = "PLC-1", Address = "A1" };
        var plcTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:05Z");
        var badTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:10Z");
        var disconnectTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:15Z");

        engine.Apply([new DriverReadResult("T1", 72.5, TagQuality.Good, plcTimestamp)]);
        engine.Apply([new DriverReadResult("T1", -1, TagQuality.Bad, badTimestamp)]);
        engine.MarkDeviceDisconnected([tag], disconnectTimestamp);

        Assert.True(cache.TryGet("T1", out var value));
        Assert.Equal(72.5, value!.Value);
        Assert.Equal(TagQuality.Disconnected, value.Quality);
        Assert.Equal(plcTimestamp, value.Timestamp);
    }

    [Fact]
    public void GoodUncertainDisconnectPreservesLastGoodValueAndTimestamp()
    {
        var cache = new TagCache();
        var engine = new TagEngine(cache);
        var tag = new TagDefinition { Id = "T1", DeviceId = "PLC-1", Address = "A1" };
        var plcTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:05Z");
        var uncertainTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:10Z");
        var disconnectTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:15Z");

        engine.Apply([new DriverReadResult("T1", 72.5, TagQuality.Good, plcTimestamp)]);
        engine.Apply([new DriverReadResult("T1", 0, TagQuality.Uncertain, uncertainTimestamp)]);
        engine.MarkDeviceDisconnected([tag], disconnectTimestamp);

        Assert.True(cache.TryGet("T1", out var value));
        Assert.Equal(72.5, value!.Value);
        Assert.Equal(TagQuality.Disconnected, value.Quality);
        Assert.Equal(plcTimestamp, value.Timestamp);
    }

    [Fact]
    public void BadBeforeAnyGoodDoesNotFabricateAValue()
    {
        var cache = new TagCache();
        var engine = new TagEngine(cache);
        var tag = new TagDefinition { Id = "T1", DeviceId = "PLC-1", Address = "A1" };
        var badTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:10Z");
        var disconnectTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:15Z");

        engine.Apply([new DriverReadResult("T1", 72.5, TagQuality.Bad, badTimestamp)]);
        engine.MarkDeviceDisconnected([tag], disconnectTimestamp);

        Assert.True(cache.TryGet("T1", out var value));
        Assert.Null(value!.Value);
        Assert.Equal(TagQuality.Disconnected, value.Quality);
        Assert.Equal(disconnectTimestamp, value.Timestamp);
    }

    [Fact]
    public void RepeatedDisconnectAfterNonGoodTransitionPreservesLastGoodValue()
    {
        var cache = new TagCache();
        var engine = new TagEngine(cache);
        var tag = new TagDefinition { Id = "T1", DeviceId = "PLC-1", Address = "A1" };
        var plcTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:05Z");
        var firstDisconnect = DateTimeOffset.Parse("2026-01-01T00:00:15Z");
        var secondDisconnect = DateTimeOffset.Parse("2026-01-01T00:00:20Z");

        engine.Apply([new DriverReadResult("T1", 72.5, TagQuality.Good, plcTimestamp)]);
        engine.Apply([new DriverReadResult("T1", null, TagQuality.Bad, DateTimeOffset.Parse("2026-01-01T00:00:10Z"))]);
        engine.MarkDeviceDisconnected([tag], firstDisconnect);
        engine.MarkDeviceDisconnected([tag], secondDisconnect);

        Assert.True(cache.TryGet("T1", out var value));
        Assert.Equal(72.5, value!.Value);
        Assert.Equal(TagQuality.Disconnected, value.Quality);
        Assert.Equal(plcTimestamp, value.Timestamp);
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
