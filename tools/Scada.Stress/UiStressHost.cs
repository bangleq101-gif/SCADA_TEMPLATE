using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Runtime.Tags;

namespace Scada.Stress;

public sealed class UiStressHost : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<object?> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TagCache _cache;
    private readonly RuntimeOptions _options;
    private Dispatcher? _dispatcher;
    private MonitoringViewModel? _viewModel;
    private DispatcherResponsivenessProbe? _probe;
    private long _updates;

    public UiStressHost(TagCache cache, RuntimeOptions options)
    {
        _cache = cache; _options = options;
        _thread = new Thread(Run) { IsBackground = true, Name = "SCADA Stress UI" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public async Task StartAsync() { _thread.Start(); await _started.Task.ConfigureAwait(false); }
    public void BeginMeasurement() => _dispatcher!.Invoke(() => { Interlocked.Exchange(ref _updates, 0); _probe?.Dispose(); _probe = new DispatcherResponsivenessProbe(_dispatcher); });
    public void PostHeartbeat() => _probe?.Post();
    public DispatcherStressSummary Snapshot => _dispatcher!.Invoke(() => new DispatcherStressSummary(
        Interlocked.Read(ref _updates), _cache.Snapshot.SubscriptionCount,
        PollingMetricsCollector.Summarize(_probe?.Snapshot.LatencyMicroseconds ?? new BoundedHistogram()),
        _probe?.Snapshot.HeartbeatGaps ?? 0));
    private void Run()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            _viewModel = new MonitoringViewModel(_cache, _options);
            _viewModel.Activate();
            foreach (var row in _viewModel.Rows) row.PropertyChanged += OnRowChanged;
            _probe = new DispatcherResponsivenessProbe(_dispatcher);
            _started.TrySetResult(null);
            Dispatcher.Run();
        }
        catch (Exception exception) { _started.TrySetException(exception); }
    }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(TagRowViewModel.Value)) Interlocked.Increment(ref _updates); }
    public void Dispose()
    {
        if (_dispatcher is null) return;
        _dispatcher.Invoke(() => { _viewModel?.Deactivate(); _viewModel?.Dispose(); _probe?.Dispose(); _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); });
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
