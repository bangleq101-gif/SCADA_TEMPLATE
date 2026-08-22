using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.History;
using Scada.Infrastructure.History;
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

    private sealed class DisabledHistoryStore : IHistoryStore
    {
        public HistoryStorePreflightResult Preflight() =>
            new(HistoryStorePreflightStatus.Ready);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteBatchAsync(IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<HistorySample>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HistorySample>>([]);
    }
}
