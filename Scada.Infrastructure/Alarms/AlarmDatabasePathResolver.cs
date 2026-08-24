using Scada.Infrastructure.Persistence;

namespace Scada.Infrastructure.Alarms;

public static class AlarmDatabasePathResolver
{
    public static string Resolve(ProjectPath? projectPath, string configuredPath)
    {
        if (projectPath is null)
            throw new AlarmStoreException("PROJECT_PATH_REQUIRED", "Alarm persistence requires a canonical project path.");
        if (string.IsNullOrWhiteSpace(configuredPath) || Path.IsPathRooted(configuredPath))
            throw new AlarmStoreException("ALARM_DATABASE_PATH_INVALID", "Alarm database path must be a non-empty project-relative path.");

        var root = Path.GetFullPath(projectPath.DirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, configuredPath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new AlarmStoreException("ALARM_DATABASE_PATH_OUTSIDE_PROJECT", "Alarm database path must remain under the canonical project directory.");
        return fullPath;
    }
}
