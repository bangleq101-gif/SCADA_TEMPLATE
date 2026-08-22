using Scada.Core.History;
using Scada.Infrastructure.Persistence;

namespace Scada.Infrastructure.History;

public static class SqliteHistoryPathResolver
{
    public static string Resolve(ProjectPath? projectPath, string configuredPath)
    {
        if (projectPath is null)
        {
            throw new HistoryStorePermanentException(
                "PROJECT_PATH_REQUIRED",
                "Historian requires a canonical project path.");
        }

        if (string.IsNullOrWhiteSpace(configuredPath) || Path.IsPathRooted(configuredPath))
        {
            throw new HistoryStorePermanentException(
                "HISTORIAN_DATABASE_PATH_INVALID",
                "Historian database path must be a non-empty project-relative path.");
        }

        var root = Path.GetFullPath(projectPath.DirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, configuredPath));
        var rootPrefix = root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new HistoryStorePermanentException(
                "HISTORIAN_DATABASE_PATH_OUTSIDE_PROJECT",
                "Historian database path must remain under the canonical project directory.");
        }

        return fullPath;
    }
}
