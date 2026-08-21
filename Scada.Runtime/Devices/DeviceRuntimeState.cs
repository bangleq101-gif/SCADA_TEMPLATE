namespace Scada.Runtime.Devices;

public sealed class DeviceRuntimeState
{
    private readonly object _sync = new();

    public DeviceRuntimeState(string deviceId) => DeviceId = deviceId;

    public string DeviceId { get; }
    public string ConnectionState { get; private set; } = "Disconnected";
    public string? LastError { get; private set; }
    public DateTimeOffset? LastSuccessfulRead { get; private set; }
    public long ReadCount { get; private set; }

    public void MarkConnected()
    {
        lock (_sync)
        {
            ConnectionState = "Connected";
            LastError = null;
        }
    }

    public void MarkSuccess(DateTimeOffset timestamp)
    {
        lock (_sync)
        {
            ConnectionState = "Connected";
            LastError = null;
            LastSuccessfulRead = timestamp;
            ReadCount++;
        }
    }

    public void MarkFailure(Exception exception)
    {
        lock (_sync)
        {
            ConnectionState = "Faulted";
            LastError = exception.Message;
        }
    }
}
