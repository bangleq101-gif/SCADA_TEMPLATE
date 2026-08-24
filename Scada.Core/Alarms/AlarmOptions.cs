namespace Scada.Core.Alarms;

public sealed class AlarmOptions
{
    public bool Enabled { get; set; }
    public bool PersistenceEnabled { get; set; } = true;
    public string DatabasePath { get; set; } = "Data/alarms.db";
    public int QueueCapacity { get; set; } = 16_384;
    public int BatchSize { get; set; } = 256;
    public int FlushIntervalMilliseconds { get; set; } = 500;
    public int ShutdownDrainTimeoutMilliseconds { get; set; } = 5_000;
    public List<AlarmDefinition> Definitions { get; set; } = [];
}
