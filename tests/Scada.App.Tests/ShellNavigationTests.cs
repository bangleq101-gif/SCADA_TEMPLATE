using Scada.App.ViewModels;
using Scada.Core.Configuration;
using Xunit;

namespace Scada.App.Tests;

public sealed class ShellNavigationTests
{
    [Fact]
    public void NavigationHierarchyContainsFourWorkspaceGroups()
    {
        var context = CreateContext();

        Assert.Equal(4, context.Shell.NavigationItems.Count);
        Assert.All(context.Shell.NavigationItems, group => Assert.True(group.IsGroup));
        Assert.All(context.Shell.NavigationItems, group => Assert.Single(group.Children));
    }

    [Fact]
    public void SelectedStateHasNoPublicSetter()
    {
        var property = typeof(NavigationItem).GetProperty(nameof(NavigationItem.IsSelected));

        Assert.NotNull(property);
        Assert.Null(property!.GetSetMethod());
    }

    [Fact]
    public void EachWorkspaceGroupHasTheExpectedCanonicalLeaf()
    {
        var context = CreateContext();

        var routeKeys = context.Shell.NavigationItems
            .SelectMany(group => group.Children)
            .Select(item => item.RouteKey!)
            .ToArray();

        Assert.Equal(
            [
                NavigationService.OperationOverviewRoute,
                NavigationService.MachineSettingsOverviewRoute,
                NavigationService.MonitoringOnlineTagsRoute,
                NavigationService.EngineeringOverviewRoute
            ],
            routeKeys);
    }

    [Fact]
    public void InitialRouteActivatesOperationAndSelectsOneLeaf()
    {
        var context = CreateContext();

        Assert.Equal(NavigationService.OperationOverviewRoute, context.Navigation.CurrentRouteKey);
        Assert.Same(context.Operation, context.Navigation.CurrentViewModel);
        Assert.True(context.Operation.IsActive);
        Assert.Equal(1, SelectedLeafCount(context.Shell));
        Assert.True(SelectedLeaf(context.Shell).IsSelected);
    }

    [Fact]
    public void ValidNavigationChangesRouteViewModelAndSelection()
    {
        var context = CreateContext();

        context.Shell.NavigateCommand.Execute(
            context.Shell.NavigationItems[2].Children[0]);

        Assert.Equal(NavigationService.MonitoringOnlineTagsRoute, context.Navigation.CurrentRouteKey);
        Assert.Same(context.Monitoring, context.Navigation.CurrentViewModel);
        Assert.Equal(1, SelectedLeafCount(context.Shell));
        Assert.Equal(
            NavigationService.MonitoringOnlineTagsRoute,
            SelectedLeaf(context.Shell).RouteKey);
        Assert.Equal(2, context.Cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void InvalidRouteLeavesNavigationStateAndSelectionUnchanged()
    {
        var context = CreateContext();
        context.Navigation.Navigate(NavigationService.MonitoringOnlineTagsRoute);
        var previousRoute = context.Navigation.CurrentRouteKey;
        var previousViewModel = context.Navigation.CurrentViewModel;
        var previousSelected = SelectedLeaf(context.Shell).RouteKey;
        var subscriptions = context.Cache.TotalSubscriptionCount;

        context.Navigation.Navigate("not-a-route");

        Assert.Equal(previousRoute, context.Navigation.CurrentRouteKey);
        Assert.Same(previousViewModel, context.Navigation.CurrentViewModel);
        Assert.Equal(previousSelected, SelectedLeaf(context.Shell).RouteKey);
        Assert.Equal(subscriptions, context.Cache.TotalSubscriptionCount);
    }

    [Fact]
    public void NavigatingToCurrentRouteDoesNotReactivateWorkspace()
    {
        var context = CreateContext();
        context.Navigation.Navigate(NavigationService.MonitoringOnlineTagsRoute);
        var subscriptions = context.Cache.TotalSubscriptionCount;

        context.Navigation.Navigate(NavigationService.MonitoringOnlineTagsRoute);

        Assert.Equal(subscriptions, context.Cache.TotalSubscriptionCount);
        Assert.Equal(2, context.Cache.ActiveSubscriptionCount);
    }

    [Fact]
    public void NavigationAwayDeactivatesOldWorkspaceAndBackActivatesItOnce()
    {
        var context = CreateContext();

        context.Navigation.Navigate(NavigationService.MonitoringOnlineTagsRoute);
        Assert.Equal(2, context.Cache.ActiveSubscriptionCount);

        context.Navigation.Navigate(NavigationService.EngineeringOverviewRoute);
        Assert.Equal(0, context.Cache.ActiveSubscriptionCount);

        context.Navigation.Navigate(NavigationService.MonitoringOnlineTagsRoute);
        Assert.Equal(2, context.Cache.ActiveSubscriptionCount);
        Assert.Equal(4, context.Cache.TotalSubscriptionCount);
    }

    private static TestContext CreateContext()
    {
        var options = new RuntimeOptions
        {
            Tags =
            [
                new() { Id = "T1", Name = "Tag 1", DeviceId = "TEST" },
                new() { Id = "T2", Name = "Tag 2", DeviceId = "TEST" }
            ]
        };
        var cache = new TestTagCache();
        var operation = new OperationViewModel(options);
        var machineSettings = new MachineSettingsViewModel();
        var monitoring = new MonitoringViewModel(cache, options);
        var engineering = new EngineeringViewModel();
        var navigation = new NavigationService(operation, machineSettings, monitoring, engineering);
        var shell = new ShellViewModel(navigation, options);

        return new TestContext(options, cache, operation, monitoring, navigation, shell);
    }

    private static int SelectedLeafCount(ShellViewModel shell) =>
        shell.NavigationItems
            .SelectMany(group => group.Children)
            .Count(item => item.IsSelected);

    private static NavigationItem SelectedLeaf(ShellViewModel shell) =>
        shell.NavigationItems
            .SelectMany(group => group.Children)
            .Single(item => item.IsSelected);

    private sealed record TestContext(
        RuntimeOptions Options,
        TestTagCache Cache,
        OperationViewModel Operation,
        MonitoringViewModel Monitoring,
        NavigationService Navigation,
        ShellViewModel Shell);
}
