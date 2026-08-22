namespace Scada.Core.History;

public sealed class HistorianOptions
{
    public bool Enabled { get; set; }

    public string DatabasePath { get; set; } = "Data/history.db";

    public int QueueCapacity { get; set; } = 16_384;

    public int BatchSize { get; set; } = 256;

    public int FlushIntervalMilliseconds { get; set; } = 500;

    public int ShutdownDrainTimeoutMilliseconds { get; set; } = 5_000;

    public List<HistoryProfileDefinition> Profiles { get; set; } = HistoryProfileDefaults.Create();
}
