using Scada.App.Screens;
using Scada.App.ViewModels;
using Xunit;

namespace Scada.App.Tests;

public sealed class ScreenCatalogTests
{
    [Fact]
    public void DefaultCatalogCarriesStableMetadataForEveryBuiltInRoute()
    {
        var catalog = ScreenCatalog.CreateDefault();

        Assert.Equal(12, catalog.Screens.Count);
        Assert.Equal(
            catalog.Screens.Count,
            catalog.Screens.Select(screen => screen.ScreenId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.Equal(
            catalog.Screens.Count,
            catalog.Screens.Select(screen => screen.RouteKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(catalog.Screens, screen =>
        {
            Assert.False(string.IsNullOrWhiteSpace(screen.Title));
            Assert.False(string.IsNullOrWhiteSpace(screen.IconKey));
            Assert.True(screen.Order >= 0);
            Assert.False(string.IsNullOrWhiteSpace(screen.RequiredRole));
        });

        var operation = catalog.FindByScreenId("OPERATION.OVERVIEW");
        Assert.NotNull(operation);
        Assert.Equal(ScreenCategory.Operation, operation!.Category);
        Assert.Equal("operation", operation.IconKey);
    }

    [Fact]
    public void NavigationBuilderFiltersUnavailableRoutesWithoutChangingCategoryOrder()
    {
        var catalog = ScreenCatalog.CreateDefault();
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NavigationService.OperationOverviewRoute,
            NavigationService.MachineSettingsOverviewRoute,
            NavigationService.MonitoringOnlineTagsRoute,
            NavigationService.EngineeringOverviewRoute,
            NavigationService.EngineeringTagManagerRoute
        };

        var roots = catalog.BuildNavigationItems(available.Contains);

        Assert.Equal(["OPERATION", "MACHINE SETTINGS", "MONITORING", "ENGINEERING"], roots.Select(root => root.Title));
        Assert.Equal("Overview", roots[0].Children.Single().Title);
        Assert.Equal("Machine Settings", roots[1].Children.Single().Title);
        Assert.Equal("Online Tag Monitor", roots[2].Children.Single().Title);
        Assert.Equal(["Engineering Overview", "Tag Manager"], roots[3].Children.Select(item => item.Title));
        Assert.Equal("engineering.tag-manager", roots[3].Children[1].ScreenId);
        Assert.Equal("tags", roots[3].Children[1].IconKey);
    }

    [Fact]
    public void BuilderComposesModuleLineMachineHierarchyAndKeepsLeafRoutes()
    {
        var catalog = new ScreenCatalog(
        [
            new ScreenDescriptor(
                "line-2-machine",
                "Machine B",
                "operation.line-2.machine-b",
                ScreenCategory.Operation,
                "machine",
                Order: 20,
                Hierarchy: new ScreenHierarchyPath("Plant", "Line 2", "Machine B")),
            new ScreenDescriptor(
                "line-1-machine",
                "Machine A",
                "operation.line-1.machine-a",
                ScreenCategory.Operation,
                "machine",
                Order: 10,
                Hierarchy: new ScreenHierarchyPath("Plant", "Line 1", "Machine A")),
            new ScreenDescriptor(
                "utility",
                "Utilities",
                "operation.utilities",
                ScreenCategory.Operation,
                "utility",
                Order: 30,
                Hierarchy: new ScreenHierarchyPath("Plant", "Utilities"))
        ]);

        var plant = catalog.BuildNavigationItems().Single().Children.Single();
        Assert.Equal("Plant", plant.Title);
        Assert.Equal(["Line 1", "Line 2", "Utilities"], plant.Children.Select(item => item.Title));
        var machineA = plant.Children[0].Children.Single();
        var machineB = plant.Children[1].Children.Single();
        Assert.Equal("Machine A", machineA.Title);
        Assert.Equal("operation.line-1.machine-a", machineA.Children.Single().RouteKey);
        Assert.Equal("operation.line-2.machine-b", machineB.Children.Single().RouteKey);
        Assert.Equal("operation.utilities", plant.Children[2].Children.Single().RouteKey);
    }

    [Fact]
    public void HierarchyOutputIsDeterministicWhenRegistrationOrderChanges()
    {
        var screens = new[]
        {
            new ScreenDescriptor("b", "B", "route.b", ScreenCategory.Operation, "b", 20, Hierarchy: new("Plant", "Line 2")),
            new ScreenDescriptor("a", "A", "route.a", ScreenCategory.Operation, "a", 10, Hierarchy: new("Plant", "Line 1")),
            new ScreenDescriptor("c", "C", "route.c", ScreenCategory.Operation, "c", 10, Hierarchy: new("Plant", "Line 1"))
        };

        var forward = Describe(new ScreenCatalog(screens).BuildNavigationItems());
        var reverse = Describe(new ScreenCatalog(screens.Reverse()).BuildNavigationItems());

        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void InvalidCatalogMetadataIsRejectedBeforeNavigationIsBuilt()
    {
        Assert.Throws<ArgumentException>(() => new ScreenCatalog(
        [
            new ScreenDescriptor("duplicate", "One", "route.one", ScreenCategory.Operation, "one"),
            new ScreenDescriptor("DUPLICATE", "Two", "route.two", ScreenCategory.Operation, "two")
        ]));

        Assert.Throws<ArgumentException>(() => new ScreenCatalog(
        [new ScreenDescriptor(
            "orphan-line",
            "Orphan",
            "route.orphan",
            ScreenCategory.Operation,
            "screen",
            Hierarchy: new ScreenHierarchyPath(LineId: "Line 1"))]));

        Assert.Throws<ArgumentException>(() => new ScreenCatalog(
        [new ScreenDescriptor(
            "negative-order",
            "Negative",
            "route.negative",
            ScreenCategory.Operation,
            "screen",
            Order: -1)]));
    }

    [Fact]
    public void NavigationItemKeepsRouteAuthoritySeparateFromScreenMetadata()
    {
        var descriptor = new ScreenDescriptor(
            "screen-1",
            "Screen One",
            "route.one",
            ScreenCategory.Engineering,
            "engineering",
            RequiredRole: "engineer");

        var leaf = new ScreenCatalog([descriptor]).BuildNavigationItems().Single().Children.Single();

        Assert.Equal("route.one", leaf.RouteKey);
        Assert.Equal("screen-1", leaf.ScreenId);
        Assert.Equal(ScreenCategory.Engineering, leaf.Category);
        Assert.Equal("engineering", leaf.IconKey);
        Assert.Equal("engineer", leaf.RequiredRole);
        Assert.Same(descriptor, leaf.Screen);
    }

    private static string Describe(IEnumerable<NavigationItem> items) =>
        string.Join(
            "|",
            items.Select(item =>
                $"{item.Title}[{item.RouteKey}]({Describe(item.Children)})"));
}
