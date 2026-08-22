using Scada.Core.History;

namespace Scada.Infrastructure.Persistence;

public static class ProjectDocumentMigrator
{
    public static ProjectDocument MigrateToCurrent(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion == ProjectDocumentSchema.CurrentVersion)
        {
            return document;
        }

        if (document.SchemaVersion != 1)
        {
            throw new ProjectDocumentException($"Project schema version {document.SchemaVersion} cannot be migrated.");
        }

        if (document.Scada is null)
        {
            throw new ProjectDocumentException("The project document must contain a Scada configuration.");
        }

        document.Scada.Historian ??= new HistorianOptions();
        document.SchemaVersion = ProjectDocumentSchema.CurrentVersion;
        return document;
    }
}
