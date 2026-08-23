using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.History;
using Scada.Infrastructure.History;
using Scada.Infrastructure.History.Influx;
using Scada.Infrastructure.Persistence;
using Scada.Runtime.Historian;
using Scada.Runtime.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class HistorySettingsTests
{
    [Fact]
    public void BuiltinProfilesAreEditableButCannotBeRenamedOrDeleted()
    {
        var options = new RuntimeOptions();
        var session = new ProjectEditSession(options, null, null);
        var historian = new HistorianRuntimeService(
            options,
            new TestTagCache(),
            new DisabledHistoryStore(),
            NullLogger<HistorianRuntimeService>.Instance,
            TimeProvider.System);
        var viewModel = new HistorySettingsViewModel(session, historian);
        var digital = Assert.Single(viewModel.Profiles, profile => profile.Name == "Digital");
        var profileCount = viewModel.Profiles.Count;

        digital.Name = "Renamed";
        viewModel.DeleteProfile(digital);

        Assert.Equal("Digital", digital.Name);
        Assert.Equal(profileCount, viewModel.Profiles.Count);
    }

    [Fact]
    public void CustomProfileChangesWorkingProjectAndRequiresRestartAfterSaveStateChanges()
    {
        var options = new RuntimeOptions();
        var session = new ProjectEditSession(options, null, null);
        var historian = new HistorianRuntimeService(
            options,
            new TestTagCache(),
            new DisabledHistoryStore(),
            NullLogger<HistorianRuntimeService>.Instance,
            TimeProvider.System);
        var viewModel = new HistorySettingsViewModel(session, historian);

        viewModel.AddProfile("BatchAverage");

        Assert.Contains(session.WorkingProject.Historian.Profiles, profile => profile.Name == "BatchAverage");
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.RestartRequired);
    }

    [Fact]
    public void CustomProfileRenameRejectsBuiltInAndDuplicateNamesWithoutMutation()
    {
        var options = new RuntimeOptions();
        var session = new ProjectEditSession(options, null, null);
        var historian = CreateHistorian(options);
        var viewModel = new HistorySettingsViewModel(session, historian);

        viewModel.AddProfile("ProfileA");
        viewModel.AddProfile("ProfileB");
        var profileA = Assert.Single(viewModel.Profiles, profile => profile.Name == "ProfileA");

        profileA.Name = "Digital";
        Assert.Equal("ProfileA", profileA.Name);
        Assert.Contains("reserved", profileA.RenameValidationMessage, StringComparison.OrdinalIgnoreCase);

        profileA.Name = "ProfileB";
        Assert.Equal("ProfileA", profileA.Name);
        Assert.Contains("already uses", profileA.RenameValidationMessage, StringComparison.OrdinalIgnoreCase);

        profileA.Name = "RenamedProfile";
        Assert.Equal("RenamedProfile", profileA.Name);
        Assert.Null(profileA.RenameValidationMessage);
    }

    [Fact]
    public void SaveFailureIsVisibleThroughHistorySettingsStatus()
    {
        var options = new RuntimeOptions();
        var session = new ProjectEditSession(options, null, null);
        var viewModel = new HistorySettingsViewModel(session, CreateHistorian(options));

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasBlockingIssues);
        Assert.Contains("blocking", viewModel.ValidationSummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blocked", viewModel.SaveStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InfluxSettingsExposeProviderAndNeverExposeTokenContents()
    {
        var options = new RuntimeOptions();
        options.Historian.StorageProvider = HistoryStorageProvider.InfluxDb2;
        options.Historian.Influx.TokenReference = "env:SCADA_M6_TEST_TOKEN";
        Environment.SetEnvironmentVariable("SCADA_M6_TEST_TOKEN", "do-not-display-this-token");
        try
        {
            var viewModel = new HistorySettingsViewModel(
                new ProjectEditSession(options, null, null),
                CreateHistorian(options));

            viewModel.RefreshStatus();

            Assert.Equal(HistoryStorageProvider.InfluxDb2, viewModel.StorageProvider);
            Assert.Equal("Token configured", viewModel.TokenStatusText);
            Assert.DoesNotContain("do-not-display-this-token", viewModel.TokenStatusText, StringComparison.Ordinal);
            Assert.DoesNotContain("do-not-display-this-token", viewModel.InfluxTokenReference, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SCADA_M6_TEST_TOKEN", null);
        }
    }

    [Fact]
    public async Task HistoryRouteUsesTheSameHistorianServiceInstanceAsTheSettingsViewModel()
    {
        var options = new RuntimeOptions();
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddSingleton<TagCache>();
        services.AddSingleton<Scada.Runtime.Tags.ITagCache>(provider => provider.GetRequiredService<TagCache>());
        services.AddSingleton<IHistoryStore>(new DisabledHistoryStore());
        services.AddSingleton<HistorianRuntimeService>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<HistorianRuntimeService>());
        services.AddSingleton<ProjectEditSession>(new ProjectEditSession(options, null, null));
        services.AddSingleton<HistorySettingsViewModel>();

        await using var provider = services.BuildServiceProvider();
        var historian = provider.GetRequiredService<HistorianRuntimeService>();
        var viewModel = provider.GetRequiredService<HistorySettingsViewModel>();

        Assert.Same(historian, provider.GetRequiredService<IHostedService>());
        var navigation = new NavigationService(
            new OperationViewModel(options),
            new MachineSettingsViewModel(),
            new MonitoringViewModel(provider.GetRequiredService<ITagCache>(), options),
            new EngineeringViewModel(),
            historySettings: viewModel);

        Assert.True(navigation.HasRoute(NavigationService.EngineeringHistoryRoute));
        navigation.Navigate(NavigationService.EngineeringHistoryRoute);
        Assert.Same(viewModel, navigation.CurrentViewModel);
        Assert.True(viewModel.IsActive);
    }

    [Fact]
    public async Task InfluxStoreWithoutProjectPathDoesNotPreventDisabledHistorianStartup()
    {
        var options = new RuntimeOptions();
        options.Historian.Enabled = false;
        options.Historian.StorageProvider = HistoryStorageProvider.InfluxDb2;
        options.Historian.Influx.Organization = "org";
        options.Historian.Influx.Bucket = "bucket";
        options.Historian.Influx.TokenReference = "env:SCADA_INFLUX_TOKEN";
        var tagCache = new TestTagCache();
        await using var store = new BufferedInfluxHistoryStore(
            null,
            options.Historian,
            NullLogger<BufferedInfluxHistoryStore>.Instance,
            TimeProvider.System);
        var historian = new HistorianRuntimeService(
            options,
            tagCache,
            store,
            NullLogger<HistorianRuntimeService>.Instance,
            TimeProvider.System);

        await historian.StartAsync(CancellationToken.None);

        Assert.Equal(HistorianRuntimeState.Disabled, historian.Snapshot.State);
        Assert.Null(store.BufferPath);
        Assert.Equal(0, tagCache.ActiveSubscriptions);
        await historian.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InfluxStoreWithoutProjectPathFaultsPreflightWithoutSubscribingTags()
    {
        var options = new RuntimeOptions();
        options.Historian.Enabled = true;
        options.Historian.StorageProvider = HistoryStorageProvider.InfluxDb2;
        options.Tags.Add(new Scada.Core.Tags.TagDefinition
        {
            Id = "T1",
            Name = "Test",
            DeviceId = "SIM01",
            Address = "A1",
            DataType = Scada.Core.Tags.TagDataType.Double,
            Enabled = true,
            HistoryEnabled = true,
            HistoryProfile = "Analog"
        });
        var tagCache = new TestTagCache();
        await using var store = new BufferedInfluxHistoryStore(
            null,
            options.Historian,
            NullLogger<BufferedInfluxHistoryStore>.Instance,
            TimeProvider.System);
        var historian = new HistorianRuntimeService(
            options,
            tagCache,
            store,
            NullLogger<HistorianRuntimeService>.Instance,
            TimeProvider.System);

        await historian.StartAsync(CancellationToken.None);

        Assert.Equal(HistorianRuntimeState.Faulted, historian.Snapshot.State);
        Assert.Equal("PROJECT_PATH_REQUIRED", historian.Snapshot.LastErrorCode);
        Assert.Null(store.BufferPath);
        Assert.Equal(0, tagCache.ActiveSubscriptions);
        await historian.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ApplyRetentionUsesWorkingInfluxCandidateWithoutMutatingActiveOptions()
    {
        var options = new RuntimeOptions();
        options.Historian.Enabled = true;
        options.Historian.StorageProvider = HistoryStorageProvider.InfluxDb2;
        options.Historian.Influx.Organization = "org";
        options.Historian.Influx.Bucket = "A";
        var session = new ProjectEditSession(options, null, null);
        var manager = new TestRetentionManager();
        using var viewModel = new HistorySettingsViewModel(
            session,
            CreateHistorian(options),
            retentionManager: manager);
        viewModel.Activate();
        viewModel.InfluxBucket = "B";

        viewModel.ApplyRetentionCommand.Execute(null);
        await viewModel.ApplyRetentionCommand.ExecutionTask!;

        Assert.Equal(1, manager.CallCount);
        Assert.NotNull(manager.LastCandidate);
        Assert.Equal("B", manager.LastCandidate!.Bucket);
        Assert.Equal("A", options.Historian.Influx.Bucket);
        Assert.Equal("Retention applied.", viewModel.SaveStatusText);
    }

    [Fact]
    public async Task AsyncHistoryCommandRejectsOverlapAndDeactivationCancelsUiCompletion()
    {
        var options = new RuntimeOptions();
        options.Historian.Enabled = true;
        options.Historian.StorageProvider = HistoryStorageProvider.InfluxDb2;
        options.Historian.Influx.Organization = "org";
        options.Historian.Influx.Bucket = "A";
        var session = new ProjectEditSession(options, null, null);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new TestRetentionManager(release, started, ignoreCancellation: true);
        using var viewModel = new HistorySettingsViewModel(
            session,
            CreateHistorian(options),
            retentionManager: manager);
        viewModel.Activate();
        viewModel.InfluxBucket = "B";

        viewModel.ApplyRetentionCommand.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(viewModel.ApplyRetentionCommand.CanExecute(null));
        viewModel.ApplyRetentionCommand.Execute(null);
        Assert.Equal(1, manager.CallCount);

        viewModel.Deactivate();
        release.SetResult(true);
        await viewModel.ApplyRetentionCommand.ExecutionTask!;

        Assert.NotEqual("Retention applied.", viewModel.SaveStatusText);
    }

    private sealed class DisabledHistoryStore : IHistoryStore
    {
        public Task<HistoryStorePreflightResult> PreflightAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HistoryStorePreflightResult(HistoryStorePreflightStatus.Ready));

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<HistorySample>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HistorySample>>([]);
    }

    private static HistorianRuntimeService CreateHistorian(RuntimeOptions options) =>
        new(
            options,
            new TestTagCache(),
            new DisabledHistoryStore(),
            NullLogger<HistorianRuntimeService>.Instance,
            TimeProvider.System);

    private sealed class TestTagCache : Scada.Runtime.Tags.ITagCache
    {
        public int ActiveSubscriptions { get; private set; }

        public bool TryGet(string tagId, out Scada.Core.Tags.TagValue? value)
        {
            value = null;
            return false;
        }

        public IDisposable Subscribe(string tagId, Action<Scada.Core.Tags.TagValue> callback)
        {
            ActiveSubscriptions++;
            return new DelegateSubscription(() => ActiveSubscriptions--);
        }

        private sealed class DelegateSubscription : IDisposable
        {
            private readonly Action _dispose;
            private bool _disposed;

            public DelegateSubscription(Action dispose) => _dispose = dispose;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _dispose();
            }
        }
    }

    private sealed class TestRetentionManager : IHistoryRetentionManager
    {
        private readonly TaskCompletionSource<bool>? _release;
        private readonly TaskCompletionSource<bool>? _started;
        private readonly bool _ignoreCancellation;

        public TestRetentionManager(
            TaskCompletionSource<bool>? release = null,
            TaskCompletionSource<bool>? started = null,
            bool ignoreCancellation = false)
        {
            _release = release;
            _started = started;
            _ignoreCancellation = ignoreCancellation;
        }

        public int CallCount { get; private set; }
        public InfluxDbOptions? LastCandidate { get; private set; }

        public async Task<HistoryStoreOperationResult> ApplyAsync(
            InfluxDbOptions candidate,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCandidate = candidate;
            _started?.TrySetResult(true);
            if (_release is not null)
            {
                if (_ignoreCancellation)
                {
                    await _release.Task;
                }
                else
                {
                    await _release.Task.WaitAsync(cancellationToken);
                }
            }

            return new HistoryStoreOperationResult(true);
        }
    }
}
