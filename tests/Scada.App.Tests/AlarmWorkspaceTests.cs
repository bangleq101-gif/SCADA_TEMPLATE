using Microsoft.Extensions.Logging.Abstractions;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Alarms;
using Scada.Core.Configuration;
using Scada.Core.Tags;
using Scada.Runtime.Alarms;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class AlarmWorkspaceTests
{
    [Fact]
    public void EngineeringEditsUseProjectEditSessionAsOnlyDirtyAndSaveAuthority()
    {
        var options = CreateOptions();
        var session = new ProjectEditSession(options, null, null);
        var viewModel = new AlarmEngineeringViewModel(session);

        viewModel.AddCommand.Execute(null);
        var added = Assert.IsType<AlarmDefinitionEditorViewModel>(viewModel.SelectedDefinition);
        added.Id = "A2";
        added.Name = "Second alarm";
        added.TagId = "T1";
        added.RuleType = AlarmRuleType.HighHigh;
        added.Threshold = 95;

        Assert.True(session.IsDirty);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.RestartRequired);
        Assert.Same(session.ValidationIssues, viewModel.ValidationIssues);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        viewModel.RevertCommand.Execute(null);
        Assert.Single(viewModel.Definitions);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task MonitoringSubscribesOnlyWhileActiveAndAcknowledgesExactInstance()
    {
        var options = CreateOptions();
        var cache = new TagCache();
        await using var runtime = new AlarmRuntimeService(options, cache, null, NullLogger<AlarmRuntimeService>.Instance, TimeProvider.System);
        await runtime.StartAsync(CancellationToken.None);
        using var viewModel = new AlarmMonitoringViewModel(runtime);

        viewModel.Activate();
        cache.Upsert(new TagUpdate("T1", 90d, TagQuality.Good, DateTimeOffset.UtcNow));
        var row = Assert.Single(viewModel.Rows);
        Assert.Equal(AlarmLifecycleState.ActiveUnacknowledged, row.State);
        viewModel.SelectedAlarm = row;
        viewModel.AcknowledgeSelectedCommand.Execute(null);
        Assert.Equal(AlarmLifecycleState.ActiveAcknowledged, Assert.Single(viewModel.Rows).State);

        viewModel.Deactivate();
        var before = viewModel.Rows.Single().LastSourceSequence;
        cache.Upsert(new TagUpdate("T1", 0d, TagQuality.Good, DateTimeOffset.UtcNow));
        Assert.Equal(before, viewModel.Rows.Single().LastSourceSequence);
    }

    [Fact]
    public async Task AlarmRoutesStayWithinMonitoringAndEngineeringGroups()
    {
        var options = CreateOptions();
        var cache = new TagCache();
        await using var runtime = new AlarmRuntimeService(options, cache, null, NullLogger<AlarmRuntimeService>.Instance, TimeProvider.System);
        var operation = new OperationViewModel(options, runtime);
        var monitoring = new MonitoringViewModel(cache, options);
        var alarmMonitoring = new AlarmMonitoringViewModel(runtime);
        var alarmEngineering = new AlarmEngineeringViewModel(new ProjectEditSession(options, null, null));
        var navigation = new NavigationService(
            operation, new MachineSettingsViewModel(), monitoring, new EngineeringViewModel(),
            alarmMonitoring: alarmMonitoring, alarmEngineering: alarmEngineering);
        var shell = new ShellViewModel(navigation, options);

        Assert.True(navigation.HasRoute(NavigationService.MonitoringAlarmsRoute));
        Assert.True(navigation.HasRoute(NavigationService.EngineeringAlarmsRoute));
        Assert.Contains(shell.NavigationItems.Single(group => group.Title == "MONITORING").Children,
            item => item.RouteKey == NavigationService.MonitoringAlarmsRoute);
        Assert.Contains(shell.NavigationItems.Single(group => group.Title == "ENGINEERING").Children,
            item => item.RouteKey == NavigationService.EngineeringAlarmsRoute);
    }

    [Fact]
    public async Task OperationSummaryUsesRuntimeSnapshotsWithoutTagCacheSubscription()
    {
        var options = CreateOptions();
        var cache = new TagCache();
        await using var runtime = new AlarmRuntimeService(options, cache, null, NullLogger<AlarmRuntimeService>.Instance, TimeProvider.System);
        await runtime.StartAsync(CancellationToken.None);
        using var viewModel = new OperationViewModel(options, runtime);
        viewModel.Activate();

        cache.Upsert(new TagUpdate("T1", 90d, TagQuality.Good, DateTimeOffset.UtcNow));

        Assert.Equal(1, viewModel.ActiveAlarmCount);
        Assert.Contains("1 active", viewModel.AlarmSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, viewModel.OwnedAlarmSubscriptionCount);
        viewModel.Deactivate();
        Assert.Equal(0, viewModel.OwnedAlarmSubscriptionCount);
    }

    [Fact]
    public async Task MonitoringLoadsReadOnlyJournalThroughAlarmEventStore()
    {
        var options = CreateOptions();
        var cache = new TagCache();
        await using var runtime = new AlarmRuntimeService(options, cache, null, NullLogger<AlarmRuntimeService>.Instance, TimeProvider.System);
        var expected = new AlarmEvent(
            7, "A1", Guid.NewGuid(), AlarmEventType.Activated, AlarmSeverity.High,
            DateTimeOffset.UtcNow, "fingerprint", 3, DateTimeOffset.UtcNow);
        var store = new QueryAlarmStore([expected]);
        using var viewModel = new AlarmMonitoringViewModel(runtime, store);

        viewModel.Activate();
        await viewModel.LoadHistoryCommand.RunAsync();

        var row = Assert.Single(viewModel.History);
        Assert.Equal(expected.Sequence, row.Sequence);
        Assert.Equal(expected.InstanceId, row.InstanceId);
        Assert.Equal(1, store.QueryCount);
        Assert.Null(viewModel.HistoryErrorMessage);
    }

    [Fact]
    public async Task MonitoringReactivationOwnsExactlyOneSnapshotSubscriptionAndDisposeOwnsNone()
    {
        var options = CreateOptions();
        var cache = new TagCache();
        await using var runtime = new AlarmRuntimeService(options, cache, null, NullLogger<AlarmRuntimeService>.Instance, TimeProvider.System);
        using var viewModel = new AlarmMonitoringViewModel(runtime);

        viewModel.Activate();
        Assert.Equal(1, viewModel.OwnedSubscriptionCount);
        viewModel.Deactivate();
        Assert.Equal(0, viewModel.OwnedSubscriptionCount);
        viewModel.Activate();
        Assert.Equal(1, viewModel.OwnedSubscriptionCount);
        viewModel.Dispose();
        Assert.Equal(0, viewModel.OwnedSubscriptionCount);
    }

    private static RuntimeOptions CreateOptions() => new()
    {
        Tags = [new TagDefinition { Id = "T1", Name = "Temperature", DeviceId = "SIM", Address = "T1", DataType = TagDataType.Double }],
        Alarms = new AlarmOptions
        {
            Enabled = true,
            PersistenceEnabled = false,
            Definitions =
            [
                new AlarmDefinition { Id = "A1", Name = "High temperature", Message = "Temperature high", TagId = "T1", RuleType = AlarmRuleType.High, Threshold = 80, Deadband = 5 }
            ]
        }
    };

    private sealed class QueryAlarmStore(IReadOnlyList<AlarmEvent> events) : IAlarmEventStore
    {
        public int QueryCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AlarmRecoveryResult> LoadRecoveryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AlarmRecoveryResult(false, 0, []));
        public Task BeginUntrustedSessionAsync(AlarmStoreSessionRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistBatchAsync(AlarmPersistenceBatch batch, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CommitTrustedCheckpointAsync(AlarmStoreCheckpoint checkpoint, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmEventQuery query, CancellationToken cancellationToken)
        {
            QueryCount++;
            return Task.FromResult(events);
        }
    }
}
