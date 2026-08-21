using Microsoft.Extensions.Logging;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Drivers;
using Scada.Runtime.Devices;
using Scada.Runtime.Drivers;
using Scada.Runtime.Engine;

namespace Scada.Runtime.Polling;

public sealed class DevicePollingWorker
{
    private readonly DeviceDefinition _device;
    private readonly DevicePollingPlan _plan;
    private readonly IPlcDriverLease _lease;
    private readonly PollingOptions _options;
    private readonly TagEngine _tagEngine;
    private readonly DeviceRuntimeState _state;
    private readonly ILogger<DevicePollingWorker> _logger;
    private readonly TimeProvider _timeProvider;
    private Task? _runTask;
    private bool _connected;
    private bool _disconnectInFlight;
    private bool _stuck;
    private int _leaseDisposed;

    public DevicePollingWorker(
        DeviceDefinition device,
        DevicePollingPlan plan,
        IPlcDriverLease lease,
        PollingOptions options,
        TagEngine tagEngine,
        DeviceRuntimeState state,
        ILogger<DevicePollingWorker> logger,
        TimeProvider timeProvider)
    {
        _device = device;
        _plan = plan;
        _lease = lease;
        _options = options;
        _tagEngine = tagEngine;
        _state = state;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public Task Completion => _runTask ?? Task.CompletedTask;

    public DeviceRuntimeSnapshot Snapshot => _state.Snapshot();

    public void Start(CancellationToken cancellationToken)
    {
        if (_runTask is not null)
        {
            throw new InvalidOperationException($"Device worker '{_device.Id}' has already started.");
        }

        _runTask = RunAsync(cancellationToken);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (_runTask is not null)
        {
            try
            {
                await _runTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Device worker {DeviceId} did not stop within the shutdown budget.", _device.Id);
                return;
            }
        }

        var canDisposeLease = true;
        try
        {
            if (_disconnectInFlight)
            {
                _logger.LogWarning(
                    "Device {DeviceId} has a non-cooperative disconnect in flight; its lease remains owned by the process.",
                    _device.Id);
                canDisposeLease = false;
            }
            else if (_connected)
            {
                canDisposeLease = await DisconnectAsync(cancellationToken);
            }
        }
        finally
        {
            if (canDisposeLease)
            {
                await DisposeLeaseAsync();
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var reconnectDelay = _options.InitialReconnectDelayMilliseconds;
        var nextDue = new DateTimeOffset[_plan.Groups.Count];

        try
        {
            while (!cancellationToken.IsCancellationRequested && !_stuck)
            {
                if (!_connected)
                {
                    if (!await TryConnectAsync(cancellationToken))
                    {
                        await DelayAsync(TimeSpan.FromMilliseconds(reconnectDelay), cancellationToken);
                        reconnectDelay = NextReconnectDelay(reconnectDelay);
                        continue;
                    }

                    reconnectDelay = _options.InitialReconnectDelayMilliseconds;
                    ResetSchedule(nextDue);
                }

                if (_plan.Groups.Count == 0)
                {
                    await DelayAsync(Timeout.InfiniteTimeSpan, cancellationToken);
                    continue;
                }

                var now = _timeProvider.GetUtcNow();
                var nextIndex = FindNextDueIndex(nextDue);
                var delay = nextDue[nextIndex] - now;
                if (delay > TimeSpan.Zero)
                {
                    await DelayAsync(delay, cancellationToken);
                    continue;
                }

                for (var index = 0; index < _plan.Groups.Count; index++)
                {
                    now = _timeProvider.GetUtcNow();
                    if (nextDue[index] > now)
                    {
                        continue;
                    }

                    var group = _plan.Groups[index];
                    var scheduled = nextDue[index];
                    var missedCycles = 0L;
                    while (scheduled + group.Interval <= now)
                    {
                        scheduled += group.Interval;
                        missedCycles++;
                    }

                    nextDue[index] = scheduled + group.Interval;
                    _state.AddMissedCycles(missedCycles);

                    if (!await PollGroupAsync(group, cancellationToken))
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal worker shutdown.
        }
        catch (Exception exception)
        {
            var timestamp = _timeProvider.GetUtcNow();
            RecordFailure(exception, timestamp);
            _logger.LogError(exception, "Device worker {DeviceId} stopped unexpectedly.", _device.Id);
        }
    }

    private async Task<bool> TryConnectAsync(CancellationToken cancellationToken)
    {
        _state.MarkConnecting();
        using var operationCts = CreateOperationTokenSource(
            cancellationToken,
            _options.ConnectTimeoutMilliseconds);

        try
        {
            await _lease.Driver.ConnectAsync(_device, operationCts.Token);
            operationCts.Token.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            _connected = true;
            _state.MarkConnected();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            var exception = new TimeoutException($"Connecting to device '{_device.Id}' timed out.");
            RecordFailure(exception, _timeProvider.GetUtcNow());
            _logger.LogWarning(exception, "Connecting to device {DeviceId} timed out.", _device.Id);
            return false;
        }
        catch (Exception exception)
        {
            RecordFailure(exception, _timeProvider.GetUtcNow());
            _logger.LogWarning(exception, "Unable to connect device {DeviceId}; retrying.", _device.Id);
            return false;
        }
    }

    private async Task<bool> PollGroupAsync(
        DeviceScanGroupPlan group,
        CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        _state.MarkScanStarted(startedAt);
        using var operationCts = CreateOperationTokenSource(
            cancellationToken,
            _options.ReadTimeoutMilliseconds);

        try
        {
            var results = await _lease.Driver.ReadAsync(
                _device,
                group.Requests,
                operationCts.Token);
            operationCts.Token.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            _tagEngine.Apply(results);
            cancellationToken.ThrowIfCancellationRequested();

            var completedAt = _timeProvider.GetUtcNow();
            var sampleTimestamp = results.Count == 0
                ? completedAt
                : results.Max(result => result.Timestamp);
            _state.MarkSuccess(sampleTimestamp, completedAt, completedAt - startedAt);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            var exception = new TimeoutException(
                $"Reading scan group '{group.Name}' from device '{_device.Id}' timed out.");
            return await HandlePollingFailureAsync(exception, cancellationToken);
        }
        catch (Exception exception)
        {
            return await HandlePollingFailureAsync(exception, cancellationToken);
        }
    }

    private async Task<bool> HandlePollingFailureAsync(
        Exception exception,
        CancellationToken cancellationToken)
    {
        var timestamp = _timeProvider.GetUtcNow();
        RecordFailure(exception, timestamp);

        try
        {
            if (await DisconnectAsync(cancellationToken))
            {
                _state.MarkDisconnected();
            }
            else if (!cancellationToken.IsCancellationRequested)
            {
                _stuck = true;
                _logger.LogError(
                    "Device {DeviceId} polling stopped because disconnect did not complete cooperatively.",
                    _device.Id);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        return false;
    }

    private async Task<bool> DisconnectAsync(CancellationToken cancellationToken)
    {
        using var operationCts = CreateOperationTokenSource(
            cancellationToken,
            _options.DisconnectTimeoutMilliseconds);

        try
        {
            _disconnectInFlight = true;
            await _lease.Driver.DisconnectAsync(_device, operationCts.Token);
            _connected = false;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Disconnect cancelled for device {DeviceId}.", _device.Id);
            _connected = false;
            return true;
        }
        catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
        {
            _logger.LogWarning("Disconnect timed out for device {DeviceId}.", _device.Id);
            _connected = false;
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to disconnect device {DeviceId}.", _device.Id);
            _connected = false;
            return true;
        }
        finally
        {
            _disconnectInFlight = false;
        }
    }

    private void RecordFailure(Exception exception, DateTimeOffset timestamp)
    {
        _state.MarkFailure(exception, timestamp);
        _tagEngine.MarkDeviceDisconnected(_plan.Tags, timestamp);
    }

    private CancellationTokenSource CreateOperationTokenSource(
        CancellationToken cancellationToken,
        int timeoutMilliseconds)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeoutMilliseconds);
        return source;
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, _timeProvider, cancellationToken);
    }

    private void ResetSchedule(DateTimeOffset[] nextDue)
    {
        var now = _timeProvider.GetUtcNow();
        for (var index = 0; index < nextDue.Length; index++)
        {
            nextDue[index] = now;
        }
    }

    private int FindNextDueIndex(DateTimeOffset[] nextDue)
    {
        var index = 0;
        for (var candidate = 1; candidate < nextDue.Length; candidate++)
        {
            if (nextDue[candidate] < nextDue[index])
            {
                index = candidate;
            }
        }

        return index;
    }

    private int NextReconnectDelay(int currentDelay)
    {
        var next = Math.Min((long)_options.MaxReconnectDelayMilliseconds, (long)currentDelay * 2);
        return (int)Math.Max(_options.InitialReconnectDelayMilliseconds, next);
    }

    private async ValueTask DisposeLeaseAsync()
    {
        if (Interlocked.Exchange(ref _leaseDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _lease.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to dispose driver lease for device {DeviceId}.",
                _device.Id);
        }
    }
}
