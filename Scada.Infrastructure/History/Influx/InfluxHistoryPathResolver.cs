using Scada.Core.History;
using Scada.Infrastructure.Persistence;

namespace Scada.Infrastructure.History.Influx;

public static class InfluxHistoryPathResolver
{
    public static string Resolve(ProjectPath projectPath, string configuredPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        if (string.IsNullOrWhiteSpace(configuredPath) || Path.IsPathRooted(configuredPath))
        {
            throw new HistoryStorePermanentException(
                "INFLUX_BUFFER_PATH_INVALID",
                "InfluxDB buffer path must be a non-empty project-relative path.");
        }

        var segments = configuredPath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new HistoryStorePermanentException(
                "INFLUX_BUFFER_PATH_OUTSIDE_PROJECT",
                "InfluxDB buffer path must remain under the canonical project directory.");
        }

        var root = Path.GetFullPath(projectPath.DirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, configuredPath));
        var rootPrefix = root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new HistoryStorePermanentException(
                "INFLUX_BUFFER_PATH_OUTSIDE_PROJECT",
                "InfluxDB buffer path must remain under the canonical project directory.");
        }

        return fullPath;
    }
}
