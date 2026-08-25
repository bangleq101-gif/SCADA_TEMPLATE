using Microsoft.Extensions.Logging.Abstractions;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.Drivers;
using Scada.Core.History;
using Scada.Core.Mqtt;
using Scada.Core.Tags;
using Scada.Runtime.Alarms;
using Scada.Runtime.Drivers;
using Scada.Runtime.Engine;
using Scada.Runtime.Health;
using Scada.Runtime.Historian;
using Scada.Runtime.Mqtt;
using Scada.Runtime.Polling;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class RuntimeHealthWorkspaceTests
{
    [Fact]
    public void EngineeringSystemAndDiagnosticsHaveCanonicalReadOnlyRoutes()
    {
        using var fixture = HealthFixture.Create();
        using var presentation = new RuntimeHealthPresentationService(
            fixture.Health,
            NullLogger<RuntimeHealthPresentationService>.Instance);
        using var system = new SystemServicesViewModel(presentation, new InlineHealthDispatcher());
        using var diagnostics = new EngineeringDiagnosticsViewModel(presentation, new InlineHealthDispatcher());
        var navigation = new NavigationService(
            new OperationViewModel(fixture.Options),
            new MachineSettingsViewModel(),
            new MonitoringViewModel(fixture.Cache, fixture.Options),
            new EngineeringViewModel(),
            systemServices: system,
            engineeringDiagnostics: diagnostics);

        Assert.True(navigation.HasRoute(NavigationService.EngineeringSystemRoute));
        Assert.True(navigation.HasRoute(NavigationService.EngineeringDiagnosticsRoute));
        Assert.False(navigation.HasRoute("engineering.devices"));
    }

    [Fact]
    public void HealthWorkspaceSubscriptionsAreActiveOnlyAndDisposeCleanly()
    {
        using var fixture = HealthFixture.Create();
        using var presentation = new RuntimeHealthPresentationService(
            fixture.Health,
            NullLogger<RuntimeHealthPresentationService>.Instance);
        using var system = new SystemServicesViewModel(presentation, new InlineHealthDispatcher());

        system.Activate();
        Assert.Equal(1, system.OwnedHealthSubscriptionCount);
        system.Deactivate();
        Assert.Equal(0, system.OwnedHealthSubscriptionCount);
        system.Activate();
        system.Dispose();
        Assert.Equal(0, system.OwnedHealthSubscriptionCount);
    }

    [Fact]
    public void HealthWorkspaceCoalescesLatestSnapshotAndRejectsStaleGeneration()
    {
        using var fixture = HealthFixture.Create();
        using var presentation = new RuntimeHealthPresentationService(
            fixture.Health,
            NullLogger<RuntimeHealthPresentationService>.Instance);
        var dispatcher = new QueuedHealthDispatcher();
        using var system = new SystemServicesViewModel(presentation, dispatcher);

        system.Activate();
        dispatcher.DrainAll();
        fixture.Health.SampleOnceForTests();
        fixture.Health.SampleOnceForTests();
        Assert.Equal(1, dispatcher.PendingCount);
        system.Deactivate();
        dispatcher.DrainAll();
        Assert.Equal(0, system.OwnedHealthSubscriptionCount);
        Assert.Equal(0, system.PendingHealthDispatcherUpdateCount);
    }

    [Fact]
    public void SystemCardsExposeAccessibleTextAndUnavailableMetrics()
    {
        using var fixture = HealthFixture.Create();
        using var presentation = new RuntimeHealthPresentationService(
            fixture.Health,
            NullLogger<RuntimeHealthPresentationService>.Instance);
        using var system = new SystemServicesViewModel(presentation, new InlineHealthDispatcher());

        Assert.Contains(system.Services, card => card.Name == "PLC");
        Assert.Contains(system.Services, card => card.Name == "Historian");
        Assert.Contains(system.Services, card => card.Detail.Contains("Unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(RuntimeHealthState.Unknown, system.Services.Single(card => card.Name == "Process").State);
        Assert.All(system.Services, card => Assert.False(string.IsNullOrWhiteSpace(card.AutomationName)));
    }

    [Fact]
    public void InfluxBufferingCardExposesRawStateAndPendingCounts()
    {
        var options = new RuntimeOptions
        {
            Historian = { StorageProvider = HistoryStorageProvider.InfluxDb2 }
        };
        var diagnostics = new FixedHistoryDiagnostics(new HistoryStoreDiagnosticsSnapshot(
            HistoryStoreState.Buffering,
            123,
            4,
            0,
            0,
            0,
            0,
            0,
            0,
            DateTimeOffset.UnixEpoch,
            null,
            null));
        using var fixture = HealthFixture.Create(options, diagnostics);
        using var presentation = new RuntimeHealthPresentationService(
            fixture.Health,
            NullLogger<RuntimeHealthPresentationService>.Instance);
        using var system = new SystemServicesViewModel(presentation, new InlineHealthDispatcher());

        var card = system.Services.Single(card => card.Name == "Local Buffer");

        Assert.Equal(RuntimeHealthState.Degraded, card.State);
        Assert.Contains("Buffering", card.Detail, StringComparison.Ordinal);
        Assert.Contains("123", card.Detail, StringComparison.Ordinal);
        Assert.Contains("4", card.Detail, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(card.AutomationName));
    }

    [Fact]
    public void InfluxOnlineBufferCardShowsMeasuredZeroPending()
    {
        var options = new RuntimeOptions
        {
            Historian = { StorageProvider = HistoryStorageProvider.InfluxDb2 }
        };
        var diagnostics = new FixedHistoryDiagnostics(new HistoryStoreDiagnosticsSnapshot(
            HistoryStoreState.Online,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            DateTimeOffset.UnixEpoch,
            null,
            null));
        using var fixture = HealthFixture.Create(options, diagnostics);
        using var presentation = new RuntimeHealthPresentationService(
            fixture.Health,
            NullLogger<RuntimeHealthPresentationService>.Instance);
        using var system = new SystemServicesViewModel(presentation, new InlineHealthDispatcher());

        var card = system.Services.Single(card => card.Name == "Local Buffer");

        Assert.Equal(RuntimeHealthState.Healthy, card.State);
        Assert.Contains("Online", card.Detail, StringComparison.Ordinal);
        Assert.Contains("0 pending", card.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void SQLiteBufferCardIsExplicitlyNotApplicableWithoutFakeMetrics()
    {
        using var fixture = HealthFixture.Create();
        using var presentation = new RuntimeHealthPresentationService(
            fixture.Health,
            NullLogger<RuntimeHealthPresentationService>.Instance);
        using var system = new SystemServicesViewModel(presentation, new InlineHealthDispatcher());

        var card = system.Services.Single(card => card.Name == "Local Buffer");

        Assert.Equal(RuntimeHealthState.Disabled, card.State);
        Assert.Contains("Not applicable", card.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pending", card.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Online", card.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShellHealthCoalescesPublicationsAndUnsubscribesOnDispose()
    {
        using var fixture = HealthFixture.Create();
        using var presentation = new RuntimeHealthPresentationService(
            fixture.Health,
            NullLogger<RuntimeHealthPresentationService>.Instance);
        var navigation = new NavigationService(
            new OperationViewModel(fixture.Options),
            new MachineSettingsViewModel(),
            new MonitoringViewModel(fixture.Cache, fixture.Options),
            new EngineeringViewModel());
        var dispatcher = new QueuedHealthDispatcher();
        using var shell = new ShellViewModel(navigation, fixture.Options, presentation, dispatcher);

        dispatcher.DrainAll();
        fixture.Health.SampleOnceForTests();
        fixture.Health.SampleOnceForTests();
        fixture.Health.SampleOnceForTests();

        Assert.Equal(1, dispatcher.PendingCount);
        var latestState = fixture.Health.Snapshot.OverallState;
        dispatcher.DrainAll();
        Assert.Equal(latestState, shell.RuntimeHealthState);
        Assert.Contains($"Runtime: {latestState}", shell.HealthStatusText, StringComparison.Ordinal);

        shell.Dispose();
        fixture.Health.SampleOnceForTests();

        Assert.Equal(0, dispatcher.PendingCount);
    }

    private sealed class HealthFixture : IDisposable
    {
        private HealthFixture(RuntimeHealthService health, RuntimeOptions options, TagCache cache)
        {
            Health = health;
            Options = options;
            Cache = cache;
        }

        public RuntimeHealthService Health { get; }
        public RuntimeOptions Options { get; }
        public TagCache Cache { get; }

        public static HealthFixture Create(
            RuntimeOptions? options = null,
            IHistoryStoreDiagnostics? historyDiagnostics = null)
        {
            options ??= new RuntimeOptions
            {
                Tags = [new TagDefinition { Id = "T1", Name = "T1", DeviceId = "D1", Address = "T1" }]
            };
            var cache = new TagCache();
            var manager = new DeviceManager(
                options,
                new DriverResolver([]),
                new TagEngine(cache),
                NullLogger<DeviceManager>.Instance,
                NullLogger<DevicePollingWorker>.Instance,
                TimeProvider.System);
            var historian = new HistorianRuntimeService(
                options, cache, new NoOpHistoryStore(),
                NullLogger<HistorianRuntimeService>.Instance, TimeProvider.System);
            var mqtt = new MqttRuntimeService(
                options, cache, new NoOpMqttTransport(),
                NullLogger<MqttRuntimeService>.Instance, TimeProvider.System);
            var alarm = new AlarmRuntimeService(
                options, cache, null,
                NullLogger<AlarmRuntimeService>.Instance, TimeProvider.System);
            var health = new RuntimeHealthService(
                options, manager, cache, historian, mqtt, alarm,
                NullLogger<RuntimeHealthService>.Instance, TimeProvider.System, historyDiagnostics);
            return new HealthFixture(health, options, cache);
        }

        public void Dispose() => Health.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class InlineHealthDispatcher : IRuntimeHealthDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class QueuedHealthDispatcher : IRuntimeHealthDispatcher
    {
        private readonly Queue<Action> _actions = [];
        public int PendingCount => _actions.Count;
        public void Post(Action action) => _actions.Enqueue(action);
        public void DrainAll() { while (_actions.Count > 0) _actions.Dequeue()(); }
    }

    private sealed class FixedHistoryDiagnostics(HistoryStoreDiagnosticsSnapshot snapshot) : IHistoryStoreDiagnostics
    {
        public HistoryStoreDiagnosticsSnapshot Snapshot { get; } = snapshot;

        public Task<HistoryStoreOperationResult> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HistoryStoreOperationResult(true));
    }

    private sealed class NoOpHistoryStore : IHistoryStore
    {
        public Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready));
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<HistorySample>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HistorySample>>([]);
    }

    private sealed class NoOpMqttTransport : IMqttTransport
    {
        public bool IsConnected => false;
        public Task<MqttConnectionResult> ConnectAsync(MqttConnectRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MqttConnectionResult(false));
        public Task PublishAsync(MqttPublishRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
