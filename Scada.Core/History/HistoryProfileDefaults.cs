namespace Scada.Core.History;

public static class HistoryProfileDefaults
{
    public const string DigitalName = "Digital";
    public const string AnalogName = "Analog";
    public const string FastAnalogName = "FastAnalog";
    public const string CustomName = "Custom";

    public const int DisabledMaximumIntervalMilliseconds = 0;

    public static IReadOnlyList<string> RequiredNames { get; } =
    [
        DigitalName,
        AnalogName,
        FastAnalogName,
        CustomName
    ];

    public static List<HistoryProfileDefinition> Create()
    {
        return
        [
            new()
            {
                Name = DigitalName,
                Mode = HistoryMode.OnChange,
                Deadband = 0,
                MinimumIntervalMilliseconds = 0,
                MaximumIntervalMilliseconds = DisabledMaximumIntervalMilliseconds
            },
            new()
            {
                Name = AnalogName,
                Mode = HistoryMode.OnChangeAndPeriodic,
                Deadband = 0.1,
                MinimumIntervalMilliseconds = 1_000,
                MaximumIntervalMilliseconds = 60_000
            },
            new()
            {
                Name = FastAnalogName,
                Mode = HistoryMode.OnChangeAndPeriodic,
                Deadband = 0.01,
                MinimumIntervalMilliseconds = 100,
                MaximumIntervalMilliseconds = 5_000
            },
            new()
            {
                Name = CustomName,
                Mode = HistoryMode.OnChangeAndPeriodic,
                Deadband = 0,
                MinimumIntervalMilliseconds = 1_000,
                MaximumIntervalMilliseconds = 10_000
            }
        ];
    }
}
