using System.Collections.Concurrent;
using System.Numerics;
using Scada.Core.Configuration;
using Scada.Core.History;
using Scada.Core.Tags;

namespace Scada.Runtime.Historian;

public sealed class HistoryProfileEvaluator(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, EvaluationState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    public HistoryEvaluationResult Evaluate(
        string runtimeId,
        TagDefinition tag,
        HistoryProfileDefinition profile,
        TagValue value,
        DateTimeOffset recordedAtUtc,
        long monotonicTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeId);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(value);

        if (!HistoryValueNormalizer.TryNormalize(tag.DataType, value.Value, out var normalized, out var error))
        {
            return HistoryEvaluationResult.Invalid(error ?? "History value normalization failed.");
        }

        var state = GetState(tag.Id);
        lock (state.Sync)
        {
            if (state.HasSequence && value.Sequence <= state.LastSequence)
            {
                return HistoryEvaluationResult.Suppressed(state.NextDueTimestamp);
            }

            state.HasSequence = true;
            state.LastSequence = value.Sequence;
            if (!state.HasAccepted)
            {
                return Accept(runtimeId, tag, profile, value, normalized, recordedAtUtc, monotonicTimestamp, state);
            }

            var qualityChanged = state.LastQuality != value.Quality;
            if (qualityChanged)
            {
                return Accept(runtimeId, tag, profile, value, normalized, recordedAtUtc, monotonicTimestamp, state);
            }

            if (value.Quality != TagQuality.Good)
            {
                return HistoryEvaluationResult.Suppressed(state.NextDueTimestamp);
            }

            if (profile.Mode == HistoryMode.Periodic || !ValueChanged(tag.DataType, state.LastValue, normalized, profile.Deadband))
            {
                return HistoryEvaluationResult.Suppressed(state.NextDueTimestamp);
            }

            if (!MinimumIntervalElapsed(profile, state, monotonicTimestamp))
            {
                return HistoryEvaluationResult.Suppressed(state.NextDueTimestamp);
            }

            return Accept(runtimeId, tag, profile, value, normalized, recordedAtUtc, monotonicTimestamp, state);
        }
    }

    public HistoryEvaluationResult EvaluatePeriodic(
        string runtimeId,
        TagDefinition tag,
        HistoryProfileDefinition profile,
        TagValue value,
        DateTimeOffset recordedAtUtc,
        long monotonicTimestamp)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(value);

        if (!HistoryValueNormalizer.TryNormalize(tag.DataType, value.Value, out var normalized, out var error))
        {
            return HistoryEvaluationResult.Invalid(error ?? "History value normalization failed.");
        }

        var state = GetState(tag.Id);
        lock (state.Sync)
        {
            if (state.HasSequence && value.Sequence < state.LastSequence)
            {
                return HistoryEvaluationResult.Suppressed(state.NextDueTimestamp);
            }

            if (!state.HasAccepted || value.Quality != TagQuality.Good || profile.Mode == HistoryMode.OnChange)
            {
                state.NextDueTimestamp = null;
                return HistoryEvaluationResult.Suppressed();
            }

            if (!state.HasSequence || value.Sequence > state.LastSequence)
            {
                state.HasSequence = true;
                state.LastSequence = value.Sequence;
            }

            return Accept(runtimeId, tag, profile, value, normalized, recordedAtUtc, monotonicTimestamp, state);
        }
    }

    public long? GetNextDueTimestamp(string tagId)
    {
        if (!_states.TryGetValue(tagId, out var state))
        {
            return null;
        }

        lock (state.Sync)
        {
            return state.NextDueTimestamp;
        }
    }

    private HistoryEvaluationResult Accept(
        string runtimeId,
        TagDefinition tag,
        HistoryProfileDefinition profile,
        TagValue value,
        object? normalized,
        DateTimeOffset recordedAtUtc,
        long monotonicTimestamp,
        EvaluationState state)
    {
        state.HasAccepted = true;
        state.LastValue = normalized;
        state.LastQuality = value.Quality;
        state.LastAcceptedTimestamp = monotonicTimestamp;
        state.NextDueTimestamp = value.Quality == TagQuality.Good
            ? CalculateNextDue(profile, monotonicTimestamp)
            : null;

        var sample = new HistorySample(
            runtimeId,
            tag.Id,
            tag.DataType,
            normalized,
            value.Quality,
            value.Timestamp.ToUniversalTime(),
            recordedAtUtc.ToUniversalTime(),
            value.Sequence);

        return new HistoryEvaluationResult(sample, false, null, state.NextDueTimestamp);
    }

    private EvaluationState GetState(string tagId) =>
        _states.GetOrAdd(tagId, static _ => new EvaluationState());

    private bool MinimumIntervalElapsed(
        HistoryProfileDefinition profile,
        EvaluationState state,
        long monotonicTimestamp)
    {
        if (profile.MinimumIntervalMilliseconds == 0)
        {
            return true;
        }

        return timeProvider.GetElapsedTime(state.LastAcceptedTimestamp, monotonicTimestamp) >=
               TimeSpan.FromMilliseconds(profile.MinimumIntervalMilliseconds);
    }

    private long? CalculateNextDue(HistoryProfileDefinition profile, long monotonicTimestamp)
    {
        if (profile.MaximumIntervalMilliseconds <= 0 || profile.Mode == HistoryMode.OnChange)
        {
            return null;
        }

        var delta = profile.MaximumIntervalMilliseconds / 1000d * timeProvider.TimestampFrequency;
        var ticks = Math.Max(1L, checked((long)Math.Ceiling(delta)));
        return checked(monotonicTimestamp + ticks);
    }

    private static bool ValueChanged(TagDataType dataType, object? previous, object? current, double deadband)
    {
        if (previous is null || current is null)
        {
            return previous is not null || current is not null;
        }

        return dataType switch
        {
            TagDataType.Boolean => (bool)previous != (bool)current,
            TagDataType.String => !string.Equals((string)previous, (string)current, StringComparison.Ordinal),
            TagDataType.Int32 => IntegerDifference((int)previous, (int)current) >= RequiredIntegerDelta(deadband),
            TagDataType.Int64 => IntegerDifference((long)previous, (long)current) >= RequiredIntegerDelta(deadband),
            TagDataType.Double => DoubleChanged((double)previous, (double)current, deadband),
            _ => !Equals(previous, current)
        };
    }

    private static BigInteger IntegerDifference(int previous, int current) =>
        BigInteger.Abs((BigInteger)current - previous);

    private static BigInteger IntegerDifference(long previous, long current) =>
        BigInteger.Abs((BigInteger)current - previous);

    private static BigInteger RequiredIntegerDelta(double deadband)
    {
        if (deadband <= 0)
        {
            return BigInteger.One;
        }

        if (!double.IsFinite(deadband) || deadband > (double)decimal.MaxValue)
        {
            return BigInteger.Parse("79228162514264337593543950335", System.Globalization.CultureInfo.InvariantCulture);
        }

        return new BigInteger(decimal.Ceiling((decimal)deadband));
    }

    private static bool DoubleChanged(double previous, double current, double deadband)
    {
        if (previous == current)
        {
            return false;
        }

        return Math.Abs(current - previous) >= deadband;
    }

    private sealed class EvaluationState
    {
        public object Sync { get; } = new();
        public bool HasAccepted { get; set; }
        public bool HasSequence { get; set; }
        public long LastSequence { get; set; }
        public object? LastValue { get; set; }
        public TagQuality LastQuality { get; set; }
        public long LastAcceptedTimestamp { get; set; }
        public long? NextDueTimestamp { get; set; }
    }
}
