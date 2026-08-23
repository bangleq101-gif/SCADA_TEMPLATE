namespace Scada.Core.History;

public sealed class InfluxDbOptions
{
    public string Url { get; set; } = "http://localhost:8086";

    public string Organization { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string Measurement { get; set; } = "scada_history";

    public string TokenReference { get; set; } = "env:SCADA_INFLUX_TOKEN";

    public string BufferPath { get; set; } = "Data/influx-buffer.db";

    public int MaxBufferedSamples { get; set; } = 100_000;

    public int SyncBatchSize { get; set; } = 256;

    public int SyncIntervalMilliseconds { get; set; } = 1_000;

    public int HealthProbeIntervalMilliseconds { get; set; } = 30_000;

    public int ConnectionTimeoutMilliseconds { get; set; } = 5_000;

    public int WriteTimeoutMilliseconds { get; set; } = 10_000;

    public int QueryTimeoutMilliseconds { get; set; } = 10_000;

    public int ReconnectInitialDelayMilliseconds { get; set; } = 1_000;

    public int ReconnectMaxDelayMilliseconds { get; set; } = 30_000;

    public long RetentionSeconds { get; set; }
}
