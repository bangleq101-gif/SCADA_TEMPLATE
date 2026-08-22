using Scada.App.Services;
using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Tags;
using Xunit;

namespace Scada.App.Tests;

public sealed class TagManagerTests
{
    [Fact]
    public void EngineeringNavigationExposesOneCanonicalTagManagerRoute()
    {
        var context = CreateContext();

        context.Navigation.Navigate(NavigationService.EngineeringTagManagerRoute);

        Assert.Equal(NavigationService.EngineeringTagManagerRoute, context.Navigation.CurrentRouteKey);
        Assert.Same(context.TagManager, context.Navigation.CurrentViewModel);
        Assert.Equal(2, context.Shell.NavigationItems[3].Children.Count);
        Assert.Equal(
            NavigationService.EngineeringTagManagerRoute,
            context.Shell.NavigationItems[3].Children[1].RouteKey);
    }

    [Fact]
    public void NewUnsavedTagIsNotSubscribedToTagCache()
    {
        var context = CreateContext();
        context.TagManager.Activate();
        context.TagManager.AddCommand.Execute(null);
        var added = context.TagManager.Rows[^1];
        context.TagManager.SetSelection([added]);

        Assert.Equal(0, context.Cache.ActiveSubscriptionCount);
        Assert.Equal(TagQuality.NotConfigured, added.Quality);
        Assert.Equal("Not Loaded", added.RuntimeStatus);
    }

    [Fact]
    public void SelectedExistingTagOwnsAtMostOneSubscriptionAndDeactivationDisposesIt()
    {
        var context = CreateContext();
        context.Cache.Seed(new TagValue("T1", 12.5, TagQuality.Good, DateTimeOffset.UtcNow, 1));
        context.TagManager.Activate();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);

        Assert.Equal(1, context.Cache.ActiveSubscriptionCount);
        Assert.Equal(12.5, context.TagManager.Rows[0].Value);

        context.TagManager.SetSelection([context.TagManager.Rows[1]]);
        Assert.Equal(1, context.Cache.ActiveSubscriptionCount);
        Assert.Equal(2, context.Cache.TotalSubscriptionCount);

        context.TagManager.Deactivate();
        Assert.Equal(0, context.Cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void StaleSelectedCallbackCannotUpdateLaterSelection()
    {
        var context = CreateContext();
        context.TagManager.Activate();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);
        context.TagManager.SetSelection([context.TagManager.Rows[1]]);

        context.Cache.InvokeSubscription(
            0,
            new TagValue("T1", "stale", TagQuality.Good, DateTimeOffset.UtcNow, 2));

        Assert.Null(context.TagManager.Rows[0].Value);
        Assert.Same(context.TagManager.Rows[1], context.TagManager.SelectedRow);
    }

    [Fact]
    public void DirtyRuntimeConfigurationShowsCurrentRuntimeRestartRequired()
    {
        var context = CreateContext();
        context.Cache.Seed(new TagValue("T1", 12.5, TagQuality.Good, DateTimeOffset.UtcNow, 1));
        context.TagManager.Activate();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);

        Assert.Equal("Current runtime", context.TagManager.Rows[0].RuntimeStatus);
        context.TagManager.Rows[0].Address = "A99";

        Assert.Equal("Current runtime / restart required", context.TagManager.Rows[0].RuntimeStatus);
        Assert.Equal(12.5, context.TagManager.Rows[0].Value);
    }

    [Fact]
    public void CrudAndBulkEditOperateOnWorkingProjectOnly()
    {
        var context = CreateContext();
        context.TagManager.SetSelection([context.TagManager.Rows[0]]);

        context.TagManager.DuplicateCommand.Execute(null);
        Assert.Equal(3, context.TagManager.Rows.Count);
        Assert.Equal(3, context.Session.WorkingProject.Tags.Count);
        Assert.Equal(2, context.Session.StartupProject.Tags.Count);

        context.TagManager.SetSelection(context.TagManager.Rows.Cast<object>());
        context.TagManager.BulkSetEnabled(false);
        Assert.All(context.Session.WorkingProject.Tags, tag => Assert.False(tag.Enabled));

        context.TagManager.DeleteCommand.Execute(null);
        Assert.Empty(context.Session.WorkingProject.Tags);
        Assert.Equal(2, context.Session.StartupProject.Tags.Count);
    }

    [Fact]
    public void SearchAndFiltersUseViewWithoutRecreatingRows()
    {
        var context = CreateContext();
        var firstRow = context.TagManager.Rows[0];

        context.TagManager.SearchText = "Run";
        Assert.Single(context.TagManager.ItemsView.Cast<TagEditorRowViewModel>());
        Assert.Same(firstRow, context.TagManager.Rows[0]);

        context.TagManager.SearchText = string.Empty;
        context.TagManager.EnabledFilter = "Disabled";
        Assert.Single(context.TagManager.ItemsView.Cast<TagEditorRowViewModel>());
        Assert.Same(firstRow, context.TagManager.Rows[0]);
    }

    private static TestContext CreateContext()
    {
        var options = new RuntimeOptions
        {
            Devices =
            [
                new DeviceDefinition { Id = "SIM01", Name = "Simulator", DriverType = "Simulator" }
            ],
            Tags =
            [
                new TagDefinition
                {
                    Id = "T1",
                    Name = "Pump Run",
                    DeviceId = "SIM01",
                    Address = "A1",
                    Enabled = true
                },
                new TagDefinition
                {
                    Id = "T2",
                    Name = "Pump Fault",
                    DeviceId = "SIM01",
                    Address = "A2",
                    Enabled = false
                }
            ]
        };
        var cache = new TestTagCache();
        var session = new ProjectEditSession(options, null, null);
        var tagManager = new TagManagerViewModel(session, cache, new TestClipboardAdapter());
        var operation = new OperationViewModel(options);
        var machineSettings = new MachineSettingsViewModel();
        var monitoring = new MonitoringViewModel(cache, options);
        var engineering = new EngineeringViewModel();
        var navigation = new NavigationService(operation, machineSettings, monitoring, engineering, tagManager);
        var shell = new ShellViewModel(navigation, options);
        return new TestContext(options, cache, session, tagManager, navigation, shell);
    }

    private sealed record TestContext(
        RuntimeOptions Options,
        TestTagCache Cache,
        ProjectEditSession Session,
        TagManagerViewModel TagManager,
        NavigationService Navigation,
        ShellViewModel Shell);

    private sealed class TestClipboardAdapter : IClipboardAdapter
    {
        public string? Text { get; private set; }

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }
}
