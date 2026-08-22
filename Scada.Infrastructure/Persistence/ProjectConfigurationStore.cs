using System.Text.Json;
using System.Text.Json.Serialization;
using Scada.Infrastructure.Configuration;

namespace Scada.Infrastructure.Persistence;

public sealed class ProjectConfigurationStore : IProjectConfigurationStore
{
    private const int SupportedSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ProjectConfigurationStore(ProjectPath projectPath)
    {
        ProjectPath = projectPath ?? throw new ArgumentNullException(nameof(projectPath));
    }

    public ProjectPath ProjectPath { get; }

    public ProjectDocument? Load()
    {
        if (!File.Exists(ProjectPath.FullPath))
        {
            return null;
        }

        ProjectDocument? document;
        try
        {
            using var stream = File.OpenRead(ProjectPath.FullPath);
            document = JsonSerializer.Deserialize<ProjectDocument>(stream, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new ProjectDocumentException(
                $"Project document '{ProjectPath.FullPath}' contains invalid JSON.", exception);
        }
        catch (IOException exception)
        {
            throw new ProjectDocumentException(
                $"Project document '{ProjectPath.FullPath}' could not be read.", exception);
        }

        ValidateDocument(document);
        ConfigurationValidator.Validate(document!.Scada!);
        return document;
    }

    public void Save(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);
        ConfigurationValidator.Validate(document.Scada!);

        Directory.CreateDirectory(ProjectPath.DirectoryPath);
        var temporaryPath = Path.Combine(
            ProjectPath.DirectoryPath,
            $".{Path.GetFileName(ProjectPath.FullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                options: FileOptions.SequentialScan))
            {
                JsonSerializer.Serialize(stream, document, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(ProjectPath.FullPath))
            {
                File.Replace(temporaryPath, ProjectPath.FullPath, destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, ProjectPath.FullPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateDocument(ProjectDocument? document)
    {
        if (document is null)
        {
            throw new ProjectDocumentException("The project document is empty.");
        }

        if (document.SchemaVersion <= 0)
        {
            throw new ProjectDocumentException("The project document must specify a positive SchemaVersion.");
        }

        if (document.SchemaVersion > SupportedSchemaVersion)
        {
            throw new ProjectDocumentException(
                $"Project schema version {document.SchemaVersion} is newer than supported version {SupportedSchemaVersion}.");
        }

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new ProjectDocumentException(
                $"Project schema version {document.SchemaVersion} is not supported.");
        }

        if (document.Scada is null)
        {
            throw new ProjectDocumentException("The project document must contain a Scada configuration.");
        }
    }
}
