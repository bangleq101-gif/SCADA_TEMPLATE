using Microsoft.Extensions.Logging;
using Scada.Core.Configuration;
using Scada.Runtime.Devices;
using Scada.Runtime.Drivers;
using Scada.Runtime.Engine;

namespace Scada.Runtime.Polling;

public sealed class DeviceManager
{
    private readonly RuntimeOptions _options;
    private readonly IPlcDriverResolver _driverResolver;
    private readonly TagEngine _tagEngine;
    private readonly ILogger<DeviceManager> _logger;
    private readonly ILogger<DevicePollingWorker> _workerLogger;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DevicePollingWorker> _workers = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _lifetimeCts;
    private bool _started;

    public DeviceManager(
        RuntimeOptions options,
        IPlcDriverResolver driverResolver,
        TagEngine tagEngine,
        ILogger<DeviceManager> logger,
        ILogger<DevicePollingWorker> workerLogger,
        TimeProvider timeProvider)
    {
        _options = options;
        _driverResolver = driverResolver;
        _tagEngine = tagEngine;
        _logger = logger;
        _workerLogger = workerLogger;
        _timeProvider = timeProvider;
    }

    public IReadOnlyDictionary<string, DeviceRuntimeSnapshot> DeviceSnapshots =>
        _workers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Snapshot,
            StringComparer.OrdinalIgnoreCase);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _lifetimeCts = new CancellationTokenSource();

        try
        {
            foreach (var device in _options.Devices.Where(device => device.Enabled))
            {
                var plan = DevicePollingPlan.Create(device, _options.Tags, _options.ScanGroups);
                var lease = _driverResolver.Acquire(device);
                var worker = new DevicePollingWorker(
                    device,
                    plan,
                    lease,
                    _options.Polling,
                    _tagEngine,
                    new DeviceRuntimeState(device.Id),
                    _workerLogger,
                    _timeProvider);
                _workers.Add(device.Id, worker);
            }

            foreach (var worker in _workers.Values)
            {
                worker.Start(_lifetimeCts.Token);
            }

            _started = true;
        }
        catch
        {
            using var cleanupCts = new CancellationTokenSource(_options.Polling.ShutdownTimeoutMilliseconds);
            foreach (var worker in _workers.Values)
            {
                await worker.ShutdownAsync(cleanupCts.Token);
            }

            _workers.Clear();
            _lifetimeCts.Dispose();
            _lifetimeCts = null;
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started || _lifetimeCts is null)
        {
            return;
        }

        _lifetimeCts.Cancel();
        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        shutdownCts.CancelAfter(_options.Polling.ShutdownTimeoutMilliseconds);

        try
        {
            await Task.WhenAll(_workers.Values.Select(worker => worker.Completion))
                .WaitAsync(shutdownCts.Token);
        }
        catch (OperationCanceledException) when (shutdownCts.IsCancellationRequested)
        {
            _logger.LogWarning("One or more device workers did not stop within the shutdown budget.");
        }

        var completedWorkers = _workers.Values.Where(worker => worker.Completion.IsCompleted).ToArray();
        try
        {
            await Task.WhenAll(completedWorkers.Select(worker => worker.ShutdownAsync(shutdownCts.Token)));
        }
        catch (OperationCanceledException) when (shutdownCts.IsCancellationRequested)
        {
            _logger.LogWarning("Device disconnect cleanup exceeded the shutdown budget.");
        }

        _started = false;
        _lifetimeCts.Dispose();
        _lifetimeCts = null;
    }
}
