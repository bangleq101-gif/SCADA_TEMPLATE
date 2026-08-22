using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Runtime.Historian;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class HistoryProfileEvaluatorTests
{
    [Fact]
    public void FirstSampleNormalizesSourceAndRecordedTimestampsToUtc()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T12:00:00+02:00"));
        var evaluator = new HistoryProfileEvaluator(clock);
        var tag = CreateTag(TagDataType.Double);
        var source = DateTimeOffset.Parse("2026-01-01T10:00:00+02:00");
        var recorded = DateTimeOffset.Parse("2026-01-01T12:00:00+02:00");

        var result = evaluator.Evaluate(
            "Runtime01", tag, Profile("Analog", 0.1, 1_000, 60_000),
            new TagValue(tag.Id, 12.5d, TagQuality.Good, source, 1), recorded, clock.GetTimestamp());

        Assert.NotNull(result.Sample);
        Assert.Equal(12.5d, result.Sample!.Value);
        Assert.Equal(source.ToUniversalTime(), result.Sample.SourceTimestampUtc);
        Assert.Equal(recorded.ToUniversalTime(), result.Sample.RecordedAtUtc);
    }

    [Fact]
    public void SequenceDuplicateIsSuppressedButLaterSequenceWithSameValueRemainsSuppressedByOnChangeRules()
    {
        var clock = new ManualTimeProvider();
        var evaluator = new HistoryProfileEvaluator(clock);
        var tag = CreateTag(TagDataType.Double);
        var profile = Profile("Analog", 0.1, 0, 60_000);
        var value = new TagValue(tag.Id, 10d, TagQuality.Good, clock.GetUtcNow(), 7);

        Assert.NotNull(evaluator.Evaluate("Runtime01", tag, profile, value, clock.GetUtcNow(), 0).Sample);
        Assert.Null(evaluator.Evaluate("Runtime01", tag, profile, value, clock.GetUtcNow(), 0).Sample);

        var laterSequence = value with { Sequence = 8 };
        Assert.Null(evaluator.Evaluate("Runtime01", tag, profile, laterSequence, clock.GetUtcNow(), 1).Sample);
    }

    [Fact]
    public void ZeroDeadbandDoesNotRecordAnExactDoubleRepeat()
    {
        var clock = new ManualTimeProvider();
        var evaluator = new HistoryProfileEvaluator(clock);
        var tag = CreateTag(TagDataType.Double);
        var profile = Profile("Custom", 0, 0, 10_000);
        var first = new TagValue(tag.Id, 10d, TagQuality.Good, clock.GetUtcNow(), 1);
        var repeat = first with { Sequence = 2 };

        Assert.NotNull(evaluator.Evaluate("Runtime01", tag, profile, first, clock.GetUtcNow(), 0).Sample);
        Assert.Null(evaluator.Evaluate("Runtime01", tag, profile, repeat, clock.GetUtcNow(), 1).Sample);
    }

    [Fact]
    public void QualityTransitionIsImmediateAndPreservesLastKnownValue()
    {
        var clock = new ManualTimeProvider();
        var evaluator = new HistoryProfileEvaluator(clock);
        var tag = CreateTag(TagDataType.Double);
        var profile = Profile("Analog", 100, 60_000, 60_000);
        var good = new TagValue(tag.Id, 25.3d, TagQuality.Good, clock.GetUtcNow(), 1);
        var disconnect = good with
        {
            Quality = TagQuality.Disconnected,
            Sequence = 2
        };

        Assert.NotNull(evaluator.Evaluate("Runtime01", tag, profile, good, clock.GetUtcNow(), 0).Sample);
        var transition = evaluator.Evaluate("Runtime01", tag, profile, disconnect, clock.GetUtcNow(), 1);

        Assert.NotNull(transition.Sample);
        Assert.Equal(25.3d, transition.Sample!.Value);
        Assert.Equal(TagQuality.Disconnected, transition.Sample.Quality);
        Assert.Equal(good.Timestamp, transition.Sample.SourceTimestampUtc);

        var repeated = disconnect with { Sequence = 3 };
        Assert.Null(evaluator.Evaluate("Runtime01", tag, profile, repeated, clock.GetUtcNow(), 2).Sample);
    }

    [Fact]
    public void MinimumIntervalUsesMonotonicTimeWhenWallClockMovesBackwards()
    {
        var clock = new ManualTimeProvider();
        var evaluator = new HistoryProfileEvaluator(clock);
        var tag = CreateTag(TagDataType.Double);
        var profile = Profile("Analog", 0, 1_000, 60_000);
        var first = new TagValue(tag.Id, 1d, TagQuality.Good, clock.GetUtcNow(), 1);

        Assert.NotNull(evaluator.Evaluate("Runtime01", tag, profile, first, clock.GetUtcNow(), 0).Sample);
        clock.SetUtcNow(clock.GetUtcNow().AddHours(-2));
        var tooSoon = first with { Value = 2d, Timestamp = clock.GetUtcNow(), Sequence = 2 };
        Assert.Null(evaluator.Evaluate("Runtime01", tag, profile, tooSoon, clock.GetUtcNow(), 0).Sample);

        clock.Advance(TimeSpan.FromSeconds(1));
        var afterInterval = tooSoon with { Sequence = 3 };
        Assert.NotNull(evaluator.Evaluate("Runtime01", tag, profile, afterInterval, clock.GetUtcNow(), clock.GetTimestamp()).Sample);
    }

    [Fact]
    public void PeriodicFallbackUsesOneDueTimestampAndSkipsRepeatedNonGoodValues()
    {
        var clock = new ManualTimeProvider();
        var evaluator = new HistoryProfileEvaluator(clock);
        var tag = CreateTag(TagDataType.Double);
        var profile = Profile("Analog", 0.1, 1_000, 5_000);
        var good = new TagValue(tag.Id, 1d, TagQuality.Good, clock.GetUtcNow(), 1);
        var first = evaluator.Evaluate("Runtime01", tag, profile, good, clock.GetUtcNow(), 0);

        Assert.NotNull(first.Sample);
        Assert.Equal(clock.TimestampFrequency * 5, first.NextDueTimestamp);

        clock.Advance(TimeSpan.FromSeconds(5));
        var bad = good with { Quality = TagQuality.Bad, Sequence = 2 };
        Assert.NotNull(evaluator.Evaluate("Runtime01", tag, profile, bad, clock.GetUtcNow(), clock.GetTimestamp()).Sample);
        Assert.Null(evaluator.EvaluatePeriodic("Runtime01", tag, profile, bad, clock.GetUtcNow(), clock.GetTimestamp()).Sample);
        Assert.Null(evaluator.GetNextDueTimestamp(tag.Id));

        var recovery = bad with { Quality = TagQuality.Good, Sequence = 3 };
        Assert.NotNull(evaluator.Evaluate("Runtime01", tag, profile, recovery, clock.GetUtcNow(), clock.GetTimestamp()).Sample);
    }

    [Fact]
    public void Int64DeadbandHandlesOppositeExtremesWithoutOverflow()
    {
        var clock = new ManualTimeProvider();
        var evaluator = new HistoryProfileEvaluator(clock);
        var tag = CreateTag(TagDataType.Int64);
        var profile = Profile("Wide", (double)long.MaxValue, 0, 0);
        var first = new TagValue(tag.Id, long.MinValue, TagQuality.Good, clock.GetUtcNow(), 1);
        var second = first with { Value = long.MaxValue, Sequence = 2 };

        Assert.NotNull(evaluator.Evaluate("Runtime01", tag, profile, first, clock.GetUtcNow(), 0).Sample);
        Assert.NotNull(evaluator.Evaluate("Runtime01", tag, profile, second, clock.GetUtcNow(), 1).Sample);
    }

    [Fact]
    public void InvalidAndNonFiniteValuesAreRejectedBeforeQueueing()
    {
        var clock = new ManualTimeProvider();
        var evaluator = new HistoryProfileEvaluator(clock);
        var doubleTag = CreateTag(TagDataType.Double);
        var intTag = CreateTag(TagDataType.Int32, "T2");
        var profile = Profile("Custom", 0, 0, 10_000);

        var nan = evaluator.Evaluate(
            "Runtime01", doubleTag, profile,
            new TagValue(doubleTag.Id, double.NaN, TagQuality.Good, clock.GetUtcNow(), 1),
            clock.GetUtcNow(), 0);
        var wrongType = evaluator.Evaluate(
            "Runtime01", intTag, profile,
            new TagValue(intTag.Id, 12L, TagQuality.Good, clock.GetUtcNow(), 1),
            clock.GetUtcNow(), 0);

        Assert.True(nan.Rejected);
        Assert.True(wrongType.Rejected);
        Assert.Null(nan.Sample);
        Assert.Null(wrongType.Sample);
    }

    private static TagDefinition CreateTag(TagDataType dataType, string id = "T1") => new()
    {
        Id = id,
        Name = id,
        DeviceId = "SIM01",
        Address = id,
        DataType = dataType
    };

    private static HistoryProfileDefinition Profile(
        string name,
        double deadband,
        int minimumMilliseconds,
        int maximumMilliseconds) => new()
    {
        Name = name,
        Mode = maximumMilliseconds == 0 ? HistoryMode.OnChange : HistoryMode.OnChangeAndPeriodic,
        Deadband = deadband,
        MinimumIntervalMilliseconds = minimumMilliseconds,
        MaximumIntervalMilliseconds = maximumMilliseconds
    };
}
