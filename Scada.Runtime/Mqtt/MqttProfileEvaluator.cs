using Scada.Core.Mqtt;
using Scada.Core.Tags;

namespace Scada.Runtime.Mqtt;

public sealed class MqttProfileEvaluator
{
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);
    public MqttProfileEvaluator(TimeProvider timeProvider) => _timeProvider = timeProvider;
    public bool ShouldPublish(TagDefinition tag, MqttProfileDefinition profile, TagValue value)
    {
        var now = _timeProvider.GetTimestamp();
        lock (_states)
        {
            if (!_states.TryGetValue(tag.Id, out var prior)) { _states[tag.Id] = new State(value, now); return true; }
            var qualityChanged = prior.Value.Quality != value.Quality;
            var elapsed = _timeProvider.GetElapsedTime(prior.Timestamp, now).TotalMilliseconds;
            var periodicDue = profile.MaximumIntervalMilliseconds > 0 && elapsed >= profile.MaximumIntervalMilliseconds;
            var changed = HasMeaningfulChange(prior.Value.Value, value.Value, profile.Deadband);
            var modeAllows = profile.Mode is MqttPublishMode.OnChange or MqttPublishMode.OnChangeAndPeriodic && changed || profile.Mode is MqttPublishMode.Periodic or MqttPublishMode.OnChangeAndPeriodic && periodicDue;
            if (!qualityChanged && (!modeAllows || (profile.MinimumIntervalMilliseconds > 0 && elapsed < profile.MinimumIntervalMilliseconds))) return false;
            _states[tag.Id] = new State(value, now); return true;
        }
    }
    private static bool HasMeaningfulChange(object? before, object? after, double deadband)
    {
        if (before is null || after is null) return !Equals(before, after);
        if (TryNumber(before, out var left) && TryNumber(after, out var right)) return !double.IsFinite(left) || !double.IsFinite(right) || Math.Abs(right - left) > deadband;
        return !Equals(before, after);
    }
    private static bool TryNumber(object value, out double number) { switch (value) { case int i: number = i; return true; case long l: number = l; return true; case double d: number = d; return true; default: number = 0; return false; } }
    private sealed record State(TagValue Value, long Timestamp);
}
