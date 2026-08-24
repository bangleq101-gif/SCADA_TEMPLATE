using Scada.Core.History;
using Scada.Core.Alarms;

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

        while (document.SchemaVersion < ProjectDocumentSchema.CurrentVersion)
        {
            switch (document.SchemaVersion)
            {
                case 1:
                    MigrateV1ToV2(document);
                    break;
                case 2:
                    MigrateV2ToV3(document);
                    break;
                case 3:
                    MigrateV3ToV4(document);
                    break;
                case 4:
                    MigrateV4ToV5(document);
                    break;
                case 5:
                    MigrateV5ToV6(document);
                    break;
                default:
                    throw new ProjectDocumentException(
                        $"Project schema version {document.SchemaVersion} cannot be migrated.");
            }
        }

        return document;
    }

    private static void MigrateV1ToV2(ProjectDocument document)
    {
        EnsureScada(document);
        document.Scada!.Historian ??= new HistorianOptions();
        document.SchemaVersion = 2;
    }

    private static void MigrateV2ToV3(ProjectDocument document)
    {
        EnsureScada(document);
        document.Scada!.Historian ??= new HistorianOptions();
        document.Scada.Historian.StorageProvider = HistoryStorageProvider.SQLite;
        document.Scada.Historian.Influx ??= new InfluxDbOptions();
        document.SchemaVersion = 3;
    }

    private static void MigrateV3ToV4(ProjectDocument document)
    {
        EnsureScada(document);
        document.Scada!.Mqtt ??= new Scada.Core.Mqtt.MqttOptions();
        document.SchemaVersion = 4;
    }

    private static void MigrateV4ToV5(ProjectDocument document)
    {
        EnsureScada(document);
        document.Scada!.MachineSettings ??= new Scada.Core.MachineSettings.MachineSettingsOptions();
        document.SchemaVersion = 5;
    }

    private static void MigrateV5ToV6(ProjectDocument document)
    {
        EnsureScada(document);
        document.Scada!.Alarms ??= new AlarmOptions();
        document.Scada.Alarms.Enabled = false;
        document.SchemaVersion = 6;
    }

    private static void EnsureScada(ProjectDocument document)
    {
        if (document.Scada is null)
        {
            throw new ProjectDocumentException("The project document must contain a Scada configuration.");
        }
    }
}
