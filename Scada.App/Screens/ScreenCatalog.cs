using System.Collections.ObjectModel;
using Scada.App.ViewModels;

namespace Scada.App.Screens;

public sealed class ScreenCatalog
{
    private static readonly IReadOnlyList<(ScreenCategory Category, string Title)> CategoryDefinitions =
    [
        (ScreenCategory.Operation, "OPERATION"),
        (ScreenCategory.MachineSettings, "MACHINE SETTINGS"),
        (ScreenCategory.Monitoring, "MONITORING"),
        (ScreenCategory.Engineering, "ENGINEERING")
    ];

    private readonly ReadOnlyCollection<ScreenDescriptor> _screens;

    public ScreenCatalog(IEnumerable<ScreenDescriptor> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        var materialized = screens.ToList();
        Validate(materialized);
        _screens = new ReadOnlyCollection<ScreenDescriptor>(
            materialized
                .OrderBy(screen => screen.Category)
                .ThenBy(screen => screen.Order)
                .ThenBy(screen => screen.ScreenId, StringComparer.Ordinal)
                .ToList());
    }

    public IReadOnlyList<ScreenDescriptor> Screens => _screens;

    public ScreenDescriptor? FindByScreenId(string screenId) =>
        _screens.FirstOrDefault(screen =>
            string.Equals(screen.ScreenId, screenId, StringComparison.OrdinalIgnoreCase));

    public ReadOnlyCollection<NavigationItem> BuildNavigationItems(
        Func<string, bool>? routeAvailable = null)
    {
        var availableScreens = _screens
            .Where(screen => routeAvailable is null || routeAvailable(screen.RouteKey))
            .ToArray();
        var roots = new List<NavigationItem>();

        foreach (var (category, title) in CategoryDefinitions)
        {
            var categoryScreens = availableScreens
                .Where(screen => screen.Category == category)
                .ToArray();
            if (categoryScreens.Length == 0)
            {
                continue;
            }

            roots.Add(new NavigationItem(
                title,
                children: BuildCategoryChildren(categoryScreens)));
        }

        return new ReadOnlyCollection<NavigationItem>(roots);
    }

    public static ScreenCatalog CreateDefault() => new(
    [
        new(
            "operation.overview",
            "Overview",
            NavigationService.OperationOverviewRoute,
            ScreenCategory.Operation,
            "operation",
            Order: 0,
            RequiredRole: "operator"),
        new(
            "machine-settings.overview",
            "Machine Settings",
            NavigationService.MachineSettingsOverviewRoute,
            ScreenCategory.MachineSettings,
            "settings",
            Order: 0,
            RequiredRole: "engineer"),
        new(
            "monitoring.online-tags",
            "Online Tag Monitor",
            NavigationService.MonitoringOnlineTagsRoute,
            ScreenCategory.Monitoring,
            "monitoring",
            Order: 0,
            RequiredRole: "operator"),
        new(
            "monitoring.alarms",
            "Alarms",
            NavigationService.MonitoringAlarmsRoute,
            ScreenCategory.Monitoring,
            "alarm",
            Order: 10,
            RequiredRole: "operator"),
        new(
            "engineering.overview",
            "Engineering Overview",
            NavigationService.EngineeringOverviewRoute,
            ScreenCategory.Engineering,
            "engineering",
            Order: 0,
            RequiredRole: "engineer"),
        new(
            "engineering.tag-manager",
            "Tag Manager",
            NavigationService.EngineeringTagManagerRoute,
            ScreenCategory.Engineering,
            "tags",
            Order: 10,
            RequiredRole: "engineer"),
        new(
            "engineering.history",
            "History Settings",
            NavigationService.EngineeringHistoryRoute,
            ScreenCategory.Engineering,
            "history",
            Order: 20,
            RequiredRole: "engineer"),
        new(
            "engineering.mqtt",
            "MQTT Settings",
            NavigationService.EngineeringMqttRoute,
            ScreenCategory.Engineering,
            "mqtt",
            Order: 30,
            RequiredRole: "engineer"),
        new(
            "engineering.alarms",
            "Alarm Settings",
            NavigationService.EngineeringAlarmsRoute,
            ScreenCategory.Engineering,
            "alarm-settings",
            Order: 40,
            RequiredRole: "engineer"),
        new(
            "engineering.system",
            "System Health",
            NavigationService.EngineeringSystemRoute,
            ScreenCategory.Engineering,
            "system",
            Order: 50,
            RequiredRole: "engineer"),
        new(
            "engineering.diagnostics",
            "Diagnostics",
            NavigationService.EngineeringDiagnosticsRoute,
            ScreenCategory.Engineering,
            "diagnostics",
            Order: 60,
            RequiredRole: "engineer"),
        new(
            "engineering.devices",
            "Devices",
            NavigationService.EngineeringDevicesRoute,
            ScreenCategory.Engineering,
            "devices",
            Order: 70,
            RequiredRole: "engineer")
    ]);

    private static IReadOnlyList<NavigationItem> BuildCategoryChildren(
        IEnumerable<ScreenDescriptor> screens)
    {
        var root = new NavigationNode("", int.MaxValue, "");

        foreach (var screen in screens)
        {
            var node = root;
            var hierarchy = screen.Hierarchy;
            foreach (var segment in HierarchySegments(hierarchy))
            {
                var key = "group:" + segment;
                if (!node.Children.TryGetValue(key, out var child))
                {
                    child = new NavigationNode(segment, screen.Order, segment);
                    node.Children.Add(key, child);
                }

                child.Order = Math.Min(child.Order, screen.Order);
                node = child;
            }

            node.Children.Add(
                "screen:" + screen.ScreenId,
                new NavigationNode(screen.Title, screen.Order, screen.ScreenId, screen));
        }

        return root.Children.Values
            .OrderBy(node => node.Order)
            .ThenBy(node => node.SortKey, StringComparer.Ordinal)
            .Select(ToNavigationItem)
            .ToArray();
    }

    private static IEnumerable<string> HierarchySegments(ScreenHierarchyPath? hierarchy)
    {
        if (!string.IsNullOrWhiteSpace(hierarchy?.ModuleId))
        {
            yield return hierarchy.ModuleId!;
        }

        if (!string.IsNullOrWhiteSpace(hierarchy?.LineId))
        {
            yield return hierarchy.LineId!;
        }

        if (!string.IsNullOrWhiteSpace(hierarchy?.MachineId))
        {
            yield return hierarchy.MachineId!;
        }
    }

    private static NavigationItem ToNavigationItem(NavigationNode node)
    {
        if (node.Screen is not null)
        {
            return new NavigationItem(
                node.Screen.Title,
                node.Screen.RouteKey,
                screen: node.Screen);
        }

        var children = node.Children.Values
            .OrderBy(child => child.Order)
            .ThenBy(child => child.SortKey, StringComparer.Ordinal)
            .Select(ToNavigationItem)
            .ToArray();
        return new NavigationItem(node.Title, children: children);
    }

    private static void Validate(IReadOnlyList<ScreenDescriptor> screens)
    {
        var errors = new List<string>();
        AddDuplicateErrors(screens, screen => screen.ScreenId, "ScreenId", errors);
        AddDuplicateErrors(screens, screen => screen.RouteKey, "RouteKey", errors);

        for (var index = 0; index < screens.Count; index++)
        {
            var screen = screens[index];
            if (string.IsNullOrWhiteSpace(screen.ScreenId))
            {
                errors.Add($"Screen at index {index} has a blank ScreenId.");
            }

            if (string.IsNullOrWhiteSpace(screen.Title))
            {
                errors.Add($"Screen '{screen.ScreenId}' has a blank Title.");
            }

            if (string.IsNullOrWhiteSpace(screen.RouteKey))
            {
                errors.Add($"Screen '{screen.ScreenId}' has a blank RouteKey.");
            }

            if (string.IsNullOrWhiteSpace(screen.IconKey))
            {
                errors.Add($"Screen '{screen.ScreenId}' has a blank IconKey.");
            }

            if (!Enum.IsDefined(screen.Category))
            {
                errors.Add($"Screen '{screen.ScreenId}' has an unknown Category.");
            }

            if (screen.Order < 0)
            {
                errors.Add($"Screen '{screen.ScreenId}' has a negative Order.");
            }

            if (screen.RequiredRole is not null && string.IsNullOrWhiteSpace(screen.RequiredRole))
            {
                errors.Add($"Screen '{screen.ScreenId}' has a blank RequiredRole.");
            }

            var hierarchy = screen.Hierarchy;
            if (!string.IsNullOrWhiteSpace(hierarchy?.LineId)
                && string.IsNullOrWhiteSpace(hierarchy.ModuleId))
            {
                errors.Add($"Screen '{screen.ScreenId}' has a LineId without a ModuleId.");
            }

            if (!string.IsNullOrWhiteSpace(hierarchy?.MachineId)
                && string.IsNullOrWhiteSpace(hierarchy.LineId))
            {
                errors.Add($"Screen '{screen.ScreenId}' has a MachineId without a LineId.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ArgumentException(
                "Invalid screen catalog: " + string.Join(" ", errors),
                nameof(screens));
        }
    }

    private static void AddDuplicateErrors(
        IEnumerable<ScreenDescriptor> screens,
        Func<ScreenDescriptor, string> selector,
        string name,
        ICollection<string> errors)
    {
        foreach (var group in screens
                     .Where(screen => !string.IsNullOrWhiteSpace(selector(screen)))
                     .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Duplicate {name} '{group.Key}'.");
        }
    }

    private sealed class NavigationNode(
        string title,
        int order,
        string sortKey,
        ScreenDescriptor? screen = null)
    {
        public string Title { get; } = title;
        public int Order { get; set; } = order;
        public string SortKey { get; } = sortKey;
        public ScreenDescriptor? Screen { get; } = screen;
        public Dictionary<string, NavigationNode> Children { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
