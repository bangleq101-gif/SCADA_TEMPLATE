namespace Scada.App.Screens;

public enum ScreenCategory
{
    Operation = 0,
    MachineSettings = 1,
    Monitoring = 2,
    Engineering = 3
}

public sealed record ScreenHierarchyPath(
    string? ModuleId = null,
    string? LineId = null,
    string? MachineId = null);

public sealed record ScreenDescriptor(
    string ScreenId,
    string Title,
    string RouteKey,
    ScreenCategory Category,
    string IconKey,
    int Order = 0,
    string? RequiredRole = null,
    ScreenHierarchyPath? Hierarchy = null);
