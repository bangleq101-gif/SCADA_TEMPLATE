using Scada.Core.Devices;

namespace Scada.Runtime.Devices;

public sealed class DeviceRuntimeState
{
    private readonly object _sync = new();
    private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
    private string? _lastError;
    private DateTimeOffset? _lastSuccessfulRead;
    private DateTimeOffset? _lastFailureAt;
    private long _readCount;
    private long _failureCount;
    private DateTimeOffset? _lastScanStartedAt;
    private DateTimeOffset? _lastScanCompletedAt;
    private TimeSpan? _lastScanDuration;
    private long _missedCycleCount;

    public DeviceRuntimeState(string deviceId)
    {
        DeviceId = deviceId;
    }

    public string DeviceId { get; }

    public DeviceConnectionState ConnectionState
    {
        get
        {
            lock (_sync)
            {
                return _connectionState;
            }
        }
    }

    public DeviceRuntimeSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new DeviceRuntimeSnapshot(
                DeviceId,
                _connectionState,
                _lastError,
                _lastSuccessfulRead,
                _lastFailureAt,
                _readCount,
                _failureCount,
                _lastScanStartedAt,
                _lastScanCompletedAt,
                _lastScanDuration,
                _missedCycleCount);
        }
    }

    public void MarkConnecting()
    {
        lock (_sync)
        {
            _connectionState = DeviceConnectionState.Connecting;
        }
    }

    public void MarkConnected()
    {
        lock (_sync)
        {
            _connectionState = DeviceConnectionState.Connected;
            _lastError = null;
        }
    }

    public void MarkSuccess(
        DateTimeOffset sampleTimestamp,
        DateTimeOffset completedAt,
        TimeSpan duration)
    {
        lock (_sync)
        {
            _connectionState = DeviceConnectionState.Connected;
            _lastError = null;
            _lastSuccessfulRead = sampleTimestamp;
            _lastScanCompletedAt = completedAt;
            _lastScanDuration = duration;
            _readCount++;
        }
    }

    public void MarkFailure(Exception exception, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (_sync)
        {
            _connectionState = DeviceConnectionState.Faulted;
            _lastError = exception.Message;
            _lastFailureAt = timestamp;
            _failureCount++;
        }
    }

    public void MarkDisconnected()
    {
        lock (_sync)
        {
            _connectionState = DeviceConnectionState.Disconnected;
        }
    }

    public void MarkScanStarted(DateTimeOffset timestamp)
    {
        lock (_sync)
        {
            _lastScanStartedAt = timestamp;
        }
    }

    public void AddMissedCycles(long count)
    {
        if (count <= 0)
        {
            return;
        }

        lock (_sync)
        {
            _missedCycleCount += count;
        }
    }
}
