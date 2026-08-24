using Scada.Core.Alarms;
using Scada.Core.Tags;

namespace Scada.Runtime.Alarms;

internal static class AlarmEvaluator
{
    public static AlarmEvaluation Evaluate(
        AlarmDefinition definition,
        TagValue value,
        bool conditionWasActive)
    {
        if (value.Quality != TagQuality.Good)
        {
            return new AlarmEvaluation(false, conditionWasActive, value.Quality);
        }

        if (definition.RuleType == AlarmRuleType.DigitalEquals)
        {
            return value.Value is bool current && definition.DigitalExpectedValue is bool expected
                ? new AlarmEvaluation(true, current == expected, value.Quality)
                : new AlarmEvaluation(false, conditionWasActive, value.Quality);
        }

        if (!TryNumeric(value.Value, out var numeric) || definition.Threshold is not double threshold)
        {
            return new AlarmEvaluation(false, conditionWasActive, value.Quality);
        }

        var active = definition.RuleType switch
        {
            AlarmRuleType.High or AlarmRuleType.HighHigh => conditionWasActive
                ? numeric > threshold - definition.Deadband
                : numeric >= threshold,
            AlarmRuleType.Low or AlarmRuleType.LowLow => conditionWasActive
                ? numeric < threshold + definition.Deadband
                : numeric <= threshold,
            _ => conditionWasActive
        };
        return new AlarmEvaluation(true, active, value.Quality);
    }

    private static bool TryNumeric(object? value, out double numeric)
    {
        numeric = value switch
        {
            int item => item,
            long item => item,
            double item => item,
            _ => double.NaN
        };
        return double.IsFinite(numeric);
    }
}

internal readonly record struct AlarmEvaluation(
    bool IsAvailable,
    bool ConditionActive,
    TagQuality Quality);
