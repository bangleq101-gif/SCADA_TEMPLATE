namespace Scada.Infrastructure.Persistence;

public sealed record ProjectPath
{
    public ProjectPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException("A canonical project file path is required.", nameof(fullPath));
        }

        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException(
                "The canonical project file path must be absolute; resolve relative paths in an explicit launcher.",
                nameof(fullPath));
        }

        FullPath = Path.GetFullPath(fullPath);
    }

    public string FullPath { get; }

    public string DirectoryPath => Path.GetDirectoryName(FullPath)
        ?? throw new InvalidOperationException("The canonical project file has no parent directory.");
}
