using System.Globalization;
using Scada.Core.Configuration;
using Scada.Core.Devices;

namespace Scada.Drivers.Simulator;

public sealed record SimulatorFaultOptions(
    SimulatorFaultMode Mode,
    int PeriodSeconds,
    int DurationSeconds,
    int PhaseSeconds)
{
    public const string FaultModeKey = "FaultMode";
    public const string FaultPeriodSecondsKey = "FaultPeriodSeconds";
    public const string FaultDurationSecondsKey = "FaultDurationSeconds";
    public const string FaultPhaseSecondsKey = "FaultPhaseSeconds";

    public static SimulatorFaultOptions Default { get; } =
        new(SimulatorFaultMode.None, PeriodSeconds: 10, DurationSeconds: 1, PhaseSeconds: 0);

    public static bool TryParse(
        DeviceDefinition device,
        out SimulatorFaultOptions options,
        out IReadOnlyList<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(device);

        var errors = new List<ValidationIssue>();
        var values = device.ConnectionOptions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mode = SimulatorFaultMode.None;
        if (values.TryGetValue(FaultModeKey, out var modeText) &&
            !string.IsNullOrWhiteSpace(modeText) &&
            !Enum.TryParse(modeText, ignoreCase: true, out mode))
        {
            errors.Add(Error(FaultModeKey, device, $"Unknown Simulator fault mode '{modeText}'."));
        }

        var period = ReadPositive(values, FaultPeriodSecondsKey, Default.PeriodSeconds, device, errors);
        var duration = ReadPositive(values, FaultDurationSecondsKey, Default.DurationSeconds, device, errors);
        var phase = ReadNonNegative(values, FaultPhaseSecondsKey, Default.PhaseSeconds, device, errors);

        if (mode == SimulatorFaultMode.IntermittentReadFailure && duration >= period)
        {
            errors.Add(Error(
                FaultDurationSecondsKey,
                device,
                "Intermittent fault duration must be less than its period."));
        }

        options = new SimulatorFaultOptions(mode, period, duration, phase);
        issues = errors;
        return errors.Count == 0;
    }

    public bool IsFaultActive(DeviceDefinition device, DateTimeOffset now)
    {
        if (Mode != SimulatorFaultMode.IntermittentReadFailure)
        {
            return Mode is not SimulatorFaultMode.None;
        }

        var period = Math.Max(1, PeriodSeconds);
        var phase = StableHash(device.Id) % period;
        var second = (now.ToUnixTimeSeconds() + phase + PhaseSeconds) % period;
        if (second < 0)
        {
            second += period;
        }

        return second < DurationSeconds;
    }

    private static int ReadPositive(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue,
        DeviceDefinition device,
        ICollection<ValidationIssue> issues)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        issues.Add(Error(key, device, $"Simulator option '{key}' must be a positive invariant integer."));
        return defaultValue;
    }

    private static int ReadNonNegative(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue,
        DeviceDefinition device,
        ICollection<ValidationIssue> issues)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        issues.Add(Error(key, device, $"Simulator option '{key}' must be a non-negative invariant integer."));
        return defaultValue;
    }

    private static ValidationIssue Error(string propertyName, DeviceDefinition device, string message) =>
        new(
            "SIMULATOR_OPTION_INVALID",
            ValidationSeverity.Error,
            "Device",
            device.Id,
            propertyName,
            message);

    private static int StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)(hash & 0x7fffffff);
        }
    }
}
