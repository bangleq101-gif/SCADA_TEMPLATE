namespace Scada.Core.History;

public sealed class HistoryProfileDefinition
{
    public string Name { get; set; } = string.Empty;

    public HistoryMode Mode { get; set; } = HistoryMode.OnChangeAndPeriodic;

    public double Deadband { get; set; }

    public int MinimumIntervalMilliseconds { get; set; }

    public int MaximumIntervalMilliseconds { get; set; }
}
