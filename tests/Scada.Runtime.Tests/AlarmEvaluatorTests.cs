using Scada.Core.Alarms;
using Scada.Core.Tags;
using Scada.Runtime.Alarms;
using Xunit;

namespace Scada.Runtime.Tests;

public sealed class AlarmEvaluatorTests
{
    [Theory]
    [InlineData(AlarmRuleType.High, 80d, false, true)]
    [InlineData(AlarmRuleType.HighHigh, 80d, false, true)]
    [InlineData(AlarmRuleType.Low, 20d, false, true)]
    [InlineData(AlarmRuleType.LowLow, 20d, false, true)]
    public void NumericRulesActivateAtThreshold(AlarmRuleType rule, double value, bool wasActive, bool expected)
    {
        var definition = Numeric(rule);
        var result = AlarmEvaluator.Evaluate(definition, Value(value), wasActive);
        Assert.True(result.IsAvailable);
        Assert.Equal(expected, result.ConditionActive);
    }

    [Theory]
    [InlineData(AlarmRuleType.High, 75.1, true, true)]
    [InlineData(AlarmRuleType.High, 75.0, true, false)]
    [InlineData(AlarmRuleType.Low, 24.9, true, true)]
    [InlineData(AlarmRuleType.Low, 25.0, true, false)]
    public void NumericRulesUseDeterministicDeadbandReturnBoundaries(
        AlarmRuleType rule, double value, bool wasActive, bool expected)
    {
        var result = AlarmEvaluator.Evaluate(Numeric(rule), Value(value), wasActive);
        Assert.True(result.IsAvailable);
        Assert.Equal(expected, result.ConditionActive);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    public void DigitalEqualsAcceptsBooleanOnly(bool value, bool expectedValue, bool active)
    {
        var definition = new AlarmDefinition { RuleType = AlarmRuleType.DigitalEquals, DigitalExpectedValue = expectedValue };
        var result = AlarmEvaluator.Evaluate(definition, Value(value), false);
        Assert.True(result.IsAvailable);
        Assert.Equal(active, result.ConditionActive);
    }

    [Theory]
    [InlineData(TagQuality.Bad)]
    [InlineData(TagQuality.Uncertain)]
    [InlineData(TagQuality.Disconnected)]
    [InlineData(TagQuality.NotConfigured)]
    public void UnavailableQualityHoldsPreviousCondition(TagQuality quality)
    {
        var result = AlarmEvaluator.Evaluate(Numeric(AlarmRuleType.High), Value(0d, quality), true);
        Assert.False(result.IsAvailable);
        Assert.True(result.ConditionActive);
    }

    [Theory]
    [InlineData("80")]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void IncompatibleOrNonfiniteNumericValuesAreUnavailable(object value)
    {
        var result = AlarmEvaluator.Evaluate(Numeric(AlarmRuleType.High), Value(value), false);
        Assert.False(result.IsAvailable);
        Assert.False(result.ConditionActive);
    }

    private static AlarmDefinition Numeric(AlarmRuleType rule) => new()
    {
        RuleType = rule,
        Threshold = rule is AlarmRuleType.Low or AlarmRuleType.LowLow ? 20 : 80,
        Deadband = 5
    };

    private static TagValue Value(object value, TagQuality quality = TagQuality.Good) =>
        new("T1", value, quality, DateTimeOffset.UtcNow, 1);
}
