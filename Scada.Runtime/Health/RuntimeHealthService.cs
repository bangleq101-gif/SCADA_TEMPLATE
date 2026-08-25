using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scada.Core.Configuration;
using Scada.Core.History;
using Scada.Runtime.Alarms;
using Scada.Runtime.Historian;
using Scada.Runtime.Mqtt;
using Scada.Runtime.Polling;
using Scada.Runtime.Tags;

namespace Scada.Runtime.Health;

public sealed class RuntimeHealthService : IHostedService, IAsyncDisposable
{
    public static readonly TimeSpan ProductionSamplingInterval = TimeSpan.FromSeconds(1);

    private readonly RuntimeOptions _options;
    private readonly DeviceManager _deviceManager;
    private readonly TagCache _tagCache;
    private readonly HistorianRuntimeService _historian;
    private readonly MqttRuntimeService _mqtt;
    private readonly AlarmRuntimeService _alarm;
    private readonly IHistoryStoreDiagnostics? _historyDiagnostics;
    private readonly ILogger<RuntimeHealthService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IProcessTelemetrySource _processTelemetry;
    private readonly RuntimeHealthAggregator _aggregator;
    private readonly TimeSpan _samplingInterval;
    private readonly object _sync = new();
    private readonly object _subscriberSync = new();
    private readonly List<Action<RuntimeHealthSnapshot>> _subscribers = [];
    private RuntimeHealthSnapshot _snapshot;
    private CancellationTokenSource? _lifetime;
    private PeriodicTimer? _timer;
    private Task? _samplerTask;
    private long _startTimestamp;
    private bool _started;
    private ProcessTelemetryReading _previousProcessReading;
    private long _previousProcessTimestamp;
    private bool _hasPreviousProcessReading;
    private long _materializations;

    public RuntimeHealthService(
        RuntimeOptions options,
        DeviceManager deviceManager,
        TagCache tagCache,
        HistorianRuntimeService historian,
        MqttRuntimeService mqtt,
        AlarmRuntimeService alarm,
        ILogger<RuntimeHealthService> logger,
        TimeProvider timeProvider,
        IHistoryStoreDiagnostics? historyDiagnostics = null,
        IProcessTelemetrySource? processTelemetry = null,
        TimeSpan? samplingInterval = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
        _tagCache = tagCache ?? throw new ArgumentNullException(nameof(tagCache));
        _historian = historian ?? throw new ArgumentNullException(nameof(historian));
        _mqtt = mqtt ?? throw new ArgumentNullException(nameof(mqtt));
        _alarm = alarm ?? throw new ArgumentNullException(nameof(alarm));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _historyDiagnostics = historyDiagnostics;
        _processTelemetry = processTelemetry ?? new ProcessTelemetrySource();
        _aggregator = new RuntimeHealthAggregator(options);
        _samplingInterval = samplingInterval ?? ProductionSamplingInterval;
        if (_samplingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(samplingInterval));
        }

        _snapshot = CreateSnapshot(TimeSpan.Zero, ProcessTelemetrySnapshot.Unavailable);
    }

    public RuntimeHealthSnapshot Snapshot => Volatile.Read(ref _snapshot);

    internal int SamplerTaskCount => _samplerTask is { IsCompleted: false } ? 1 : 0;
    internal int TimerCount => _timer is null ? 0 : 1;
    internal long MaterializationCount => Interlocked.Read(ref _materializations);

    public IDisposable Subscribe(Action<RuntimeHealthSnapshot> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_subscriberSync)
        {
            _subscribers.Add(callback);
        }

        Notify(callback, Snapshot);
        return new Subscription(() =>
        {
            lock (_subscriberSync)
            {
                _subscribers.Remove(callback);
            }
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_started)
            {
                return Task.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _started = true;
            _startTimestamp = _timeProvider.GetTimestamp();
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _timer = new PeriodicTimer(_samplingInterval, _timeProvider);
            _samplerTask = SampleLoopAsync(_timer, _lifetime.Token);
            _snapshot = Snapshot with { OverallState = RuntimeHealthState.Starting, Uptime = TimeSpan.Zero };
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? samplerTask;
        CancellationTokenSource? lifetime;
        PeriodicTimer? timer;
        lock (_sync)
        {
            samplerTask = _samplerTask;
            lifetime = _lifetime;
            timer = _timer;
            if (!_started && samplerTask is null)
            {
                return;
            }

            if (_started)
            {
                _snapshot = Snapshot with
                {
                    OverallState = RuntimeHealthState.Stopping,
                    CapturedAtUtc = _timeProvider.GetUtcNow()
                };
            }

            _started = false;
        }

        lifetime?.Cancel();
        timer?.Dispose();
        try
        {
            if (samplerTask is not null)
            {
                await samplerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Runtime health sampler did not stop within the host shutdown budget.");
        }
        finally
        {
            if (samplerTask is null || samplerTask.IsCompleted)
            {
                ReleaseOwnedResources(samplerTask, lifetime, timer);
            }
            else
            {
                _ = ObserveSamplerCompletionAsync(samplerTask, lifetime, timer);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (_processTelemetry is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    internal RuntimeHealthSnapshot SampleOnceForTests() => SampleOnce();

    private async Task SampleLoopAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                SampleOnce();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private RuntimeHealthSnapshot SampleOnce()
    {
        var now = _timeProvider.GetUtcNow();
        var timestamp = _timeProvider.GetTimestamp();
        var reading = _processTelemetry.Read();
        var process = CreateProcessSnapshot(reading, timestamp);
        var uptime = _timeProvider.GetElapsedTime(_startTimestamp, timestamp);
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        var snapshot = _aggregator.Aggregate(
            now,
            uptime,
            _deviceManager.DeviceSnapshots,
            _tagCache.Snapshot,
            _historian.Snapshot,
            _mqtt.Snapshot,
            _alarm.Snapshot,
            _historyDiagnostics?.Snapshot,
            process);
        Volatile.Write(ref _snapshot, snapshot);
        Interlocked.Increment(ref _materializations);
        Publish(snapshot);
        return snapshot;
    }

    private ProcessTelemetrySnapshot CreateProcessSnapshot(ProcessTelemetryReading reading, long timestamp)
    {
        double? cpu = null;
        if (_hasPreviousProcessReading
            && reading.TotalProcessorTime is not null
            && _previousProcessReading.TotalProcessorTime is not null)
        {
            cpu = ProcessTelemetryCalculator.Calculate(
                _previousProcessReading,
                _previousProcessTimestamp,
                reading,
                timestamp,
                _timeProvider.TimestampFrequency,
                Math.Max(1, Environment.ProcessorCount));
        }

        _previousProcessReading = reading;
        _previousProcessTimestamp = timestamp;
        _hasPreviousProcessReading = true;
        return new ProcessTelemetrySnapshot(cpu, reading.WorkingSetBytes, cpu is not null);
    }

    private RuntimeHealthSnapshot CreateSnapshot(TimeSpan uptime, ProcessTelemetrySnapshot process) =>
        _aggregator.Aggregate(
            _timeProvider.GetUtcNow(),
            uptime,
            _deviceManager.DeviceSnapshots,
            _tagCache.Snapshot,
            _historian.Snapshot,
            _mqtt.Snapshot,
            _alarm.Snapshot,
            _historyDiagnostics?.Snapshot,
            process);

    private async Task ObserveSamplerCompletionAsync(
        Task samplerTask,
        CancellationTokenSource? lifetime,
        PeriodicTimer? timer)
    {
        try
        {
            await samplerTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Runtime health sampler completed after shutdown with an error.");
        }
        finally
        {
            ReleaseOwnedResources(samplerTask, lifetime, timer);
        }
    }

    private void ReleaseOwnedResources(
        Task? samplerTask,
        CancellationTokenSource? lifetime,
        PeriodicTimer? timer)
    {
        lock (_sync)
        {
            if (samplerTask is not null && !samplerTask.IsCompleted)
            {
                return;
            }

            if (ReferenceEquals(_samplerTask, samplerTask))
            {
                _samplerTask = null;
            }

            if (ReferenceEquals(_lifetime, lifetime))
            {
                _lifetime = null;
            }

            if (ReferenceEquals(_timer, timer))
            {
                _timer = null;
            }
        }

        lifetime?.Dispose();
    }

    private void Publish(RuntimeHealthSnapshot snapshot)
    {
        Action<RuntimeHealthSnapshot>[] subscribers;
        lock (_subscriberSync)
        {
            subscribers = _subscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            Notify(subscriber, snapshot);
        }
    }

    private void Notify(Action<RuntimeHealthSnapshot> subscriber, RuntimeHealthSnapshot snapshot)
    {
        try
        {
            subscriber(snapshot);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Runtime health subscriber failed.");
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
            }
        }
    }
}
