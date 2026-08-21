namespace Scada.Core.Configuration;

public sealed class PollingOptions
{
    public int ConnectTimeoutMilliseconds { get; set; } = 5_000;
    public int ReadTimeoutMilliseconds { get; set; } = 5_000;
    public int DisconnectTimeoutMilliseconds { get; set; } = 1_000;
    public int InitialReconnectDelayMilliseconds { get; set; } = 1_000;
    public int MaxReconnectDelayMilliseconds { get; set; } = 5_000;
    public int ShutdownTimeoutMilliseconds { get; set; } = 5_000;
}
