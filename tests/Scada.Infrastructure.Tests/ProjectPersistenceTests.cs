using System.Text.Json;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Infrastructure.Configuration;
using Scada.Infrastructure.Persistence;
using Xunit;

namespace Scada.Infrastructure.Tests;

public sealed class ProjectPersistenceTests
{
    [Fact]
    public void ResolverAcceptsExplicitAbsolutePathAndNormalizesIt()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, ".", "project.json");
            var resolved = new ProjectPathResolver().Resolve(path);

            Assert.True(Path.IsPathFullyQualified(resolved.FullPath));
            Assert.Equal(Path.GetFullPath(path), resolved.FullPath);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ResolverRejectsRelativePathInsteadOfGuessingSourceRoot()
    {
        Assert.Throws<ArgumentException>(() => new ProjectPathResolver().Resolve("project.json"));
    }

    [Fact]
    public void MissingProjectDocumentUsesBootstrapSignal()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new ProjectConfigurationStore(
                new ProjectPath(Path.Combine(directory, "project.json")));

            Assert.Null(store.Load());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ProjectCollectionsAreAuthoritativeAndPreserveOrder()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            var options = CreateOptions("T2", "T1");
            store.Save(new ProjectDocument { SchemaVersion = 1, Scada = options });

            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.Equal(["T2", "T1"], loaded!.Scada!.Tags.Select(tag => tag.Id));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void EmptyProjectTagsRemainEmptyWithoutBootstrapOverlay()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            var options = CreateOptions();
            options.Tags.Clear();
            store.Save(new ProjectDocument { SchemaVersion = 1, Scada = options });

            Assert.Empty(store.Load()!.Scada!.Tags);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ExistingProjectWithoutSchemaVersionIsRejected()
    {
        AssertSchemaRejected("{\"Scada\":{}}", "SchemaVersion");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(8)]
    public void UnsupportedProjectSchemaVersionsAreRejected(int schemaVersion)
    {
        AssertSchemaRejected($"{{\"SchemaVersion\":{schemaVersion},\"Scada\":{{}}}}", "schema");
    }

    [Fact]
    public void MalformedProjectIsRejectedWithoutBootstrapFallback()
    {
        AssertSchemaRejected("{not-json", "invalid JSON");
    }

    [Fact]
    public void CurrentSchemaWithNullAlarmOptionsIsRejectedByStructuredValidation()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "project.json");
            File.WriteAllText(path, "{\"SchemaVersion\":6,\"Scada\":{\"Alarms\":null}}");
            var store = new ProjectConfigurationStore(new ProjectPath(path));

            var exception = Assert.Throws<ConfigurationValidationException>(() => store.Load());

            Assert.Contains(exception.Issues, issue =>
                issue.Code == "ALARM_OPTIONS_REQUIRED" && issue.IsBlocking);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void FirstSaveCreatesCurrentVersionDocument()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            store.Save(new ProjectDocument { SchemaVersion = 1, Scada = CreateOptions("T1") });

            var text = File.ReadAllText(Path.Combine(directory, "project.json"));
            Assert.Contains("SchemaVersion", text, StringComparison.Ordinal);
            Assert.Equal(ProjectDocumentSchema.CurrentVersion, store.Load()!.SchemaVersion);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void LoadingV2MigratesToV7WithoutRewritingTheSourceFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "project.json");
            const string original = "{\"SchemaVersion\":2,\"Scada\":{\"RuntimeId\":\"Runtime02\",\"Historian\":{\"Enabled\":true,\"DatabasePath\":\"Data/custom.db\"},\"Devices\":[],\"Tags\":[]}}";
            File.WriteAllText(path, original);
            var store = new ProjectConfigurationStore(new ProjectPath(path));

            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.Equal(7, loaded!.SchemaVersion);
            Assert.Empty(loaded!.Scada!.MachineSettings.Pages);
            Assert.False(loaded.Scada.Alarms.Enabled);
            Assert.Empty(loaded.Scada.Alarms.Definitions);
            Assert.Equal("Runtime02", loaded.Scada!.RuntimeId);
            Assert.True(loaded.Scada.Historian.Enabled);
            Assert.Equal("Data/custom.db", loaded.Scada.Historian.DatabasePath);
            Assert.Equal(HistoryStorageProvider.SQLite, loaded.Scada.Historian.StorageProvider);
            Assert.Equal("Data/influx-buffer.db", loaded.Scada.Historian.Influx.BufferPath);
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void LoadingV1PerformsSequentialMigrationAndSaveWritesV7()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "project.json");
            const string original = "{\"SchemaVersion\":1,\"Scada\":{\"RuntimeId\":\"Runtime01\",\"Historian\":{\"Enabled\":true,\"DatabasePath\":\"Data/v1.db\"},\"Devices\":[],\"Tags\":[]}}";
            File.WriteAllText(path, original);
            var store = new ProjectConfigurationStore(new ProjectPath(path));

            var loaded = store.Load()!;

            Assert.Equal(7, loaded.SchemaVersion);
            Assert.True(loaded.Scada!.Historian.Enabled);
            Assert.Equal("Data/v1.db", loaded.Scada.Historian.DatabasePath);
            Assert.Equal(HistoryStorageProvider.SQLite, loaded.Scada.Historian.StorageProvider);
            Assert.Equal(original, File.ReadAllText(path));

            store.Save(loaded);

            Assert.Contains("\"SchemaVersion\": 7", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void LoadingV1MigratesInMemoryWithoutRewritingTheSourceFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "project.json");
            const string original = "{\"SchemaVersion\":1,\"Scada\":{\"RuntimeId\":\"Runtime01\",\"Devices\":[{\"Id\":\"SIM01\",\"DriverType\":\"Simulator\"}],\"Tags\":[]}}";
            File.WriteAllText(path, original);
            var store = new ProjectConfigurationStore(new ProjectPath(path));

            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.Equal(ProjectDocumentSchema.CurrentVersion, loaded!.SchemaVersion);
            Assert.NotEqual(original, JsonSerializer.Serialize(loaded));
            Assert.Equal(original, File.ReadAllText(path));

            store.Save(loaded);

            Assert.Contains($"\"SchemaVersion\": {ProjectDocumentSchema.CurrentVersion}", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void LoadingV6AddsSourceTypeInMemoryAndExplicitSaveWritesEngineeringFields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "project.json");
            const string original = "{\"SchemaVersion\":6,\"Scada\":{\"RuntimeId\":\"Runtime01\",\"Devices\":[{\"Id\":\"SIM01\",\"DriverType\":\"Simulator\"}],\"Tags\":[{\"Id\":\"LEVEL\",\"Name\":\"Level\",\"DeviceId\":\"SIM01\",\"Address\":\"A1\",\"DataType\":3}]}}";
            File.WriteAllText(path, original);
            var store = new ProjectConfigurationStore(new ProjectPath(path));

            var loaded = store.Load()!;
            var tag = Assert.Single(loaded.Scada!.Tags);

            Assert.Equal(7, loaded.SchemaVersion);
            Assert.Equal(TagDataType.Double, tag.DataType);
            Assert.Equal(TagDataType.Double, tag.SourceDataType);
            Assert.Equal(1d, tag.Scale);
            Assert.Equal(0d, tag.Offset);
            Assert.Equal(original, File.ReadAllText(path));

            store.Save(loaded);
            var saved = File.ReadAllText(path);
            Assert.Contains("\"SchemaVersion\": 7", saved, StringComparison.Ordinal);
            Assert.Contains("\"SourceDataType\"", saved, StringComparison.Ordinal);
            Assert.Contains("\"Scale\": 1", saved, StringComparison.Ordinal);
            Assert.Contains("\"Offset\": 0", saved, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void InvalidSaveDoesNotReplaceExistingDocument()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            store.Save(new ProjectDocument { SchemaVersion = 1, Scada = CreateOptions("Original") });
            var original = File.ReadAllText(Path.Combine(directory, "project.json"));

            var invalid = CreateOptions("Invalid");
            invalid.Tags[0].Name = string.Empty;

            Assert.Throws<ConfigurationValidationException>(() =>
                store.Save(new ProjectDocument { SchemaVersion = 1, Scada = invalid }));

            Assert.Equal(original, File.ReadAllText(Path.Combine(directory, "project.json")));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ValidationUsesGlobalCaseInsensitiveIdAndNameUniqueness()
    {
        var options = CreateOptions("T1", "t1");
        options.Tags[1].Name = options.Tags[0].Name.ToUpperInvariant();

        var issues = ConfigurationValidator.CollectIssues(options);

        Assert.Contains(issues, issue => issue.Code == "TAG_ID_DUPLICATE");
        Assert.Contains(issues, issue => issue.Code == "TAG_NAME_DUPLICATE");
    }

    [Fact]
    public void DisabledDeviceIsValidButMissingDeviceIsNot()
    {
        var disabled = CreateOptions("T1");
        disabled.Devices[0].Enabled = false;
        ConfigurationValidator.Validate(disabled);

        var missing = CreateOptions("T1");
        missing.Tags[0].DeviceId = "MISSING";
        Assert.Contains(
            ConfigurationValidator.CollectIssues(missing),
            issue => issue.Code == "TAG_DEVICE_MISSING");
    }

    [Fact]
    public void EmptyEnabledProfilesBlockButUnknownProfilesArePreservedAsWarnings()
    {
        var empty = CreateOptions("T1");
        empty.Tags[0].HistoryEnabled = true;
        empty.Tags[0].HistoryProfile = string.Empty;
        empty.Tags[0].MqttPublishEnabled = true;
        empty.Tags[0].MqttProfile = string.Empty;
        var emptyIssues = ConfigurationValidator.CollectIssues(empty);

        Assert.Contains(emptyIssues, issue => issue.Code == "HISTORY_PROFILE_REQUIRED" && issue.IsBlocking);
        Assert.Contains(emptyIssues, issue => issue.Code == "MQTT_PROFILE_REQUIRED" && issue.IsBlocking);

        var unknown = CreateOptions("T1");
        unknown.Tags[0].HistoryProfile = "FutureHistory";
        unknown.Tags[0].MqttProfile = "FutureMqtt";
        var unknownIssues = ConfigurationValidator.CollectIssues(unknown);

        Assert.Contains(unknownIssues, issue => issue.Code == "HISTORY_PROFILE_UNKNOWN" && !issue.IsBlocking);
        Assert.Contains(unknownIssues, issue => issue.Code == "MQTT_PROFILE_UNKNOWN" && !issue.IsBlocking);
    }

    [Fact]
    public void TenThousandUniqueTagsValidateWithIndexedLookups()
    {
        var options = CreateOptions();
        options.Tags = Enumerable.Range(0, 10_000)
            .Select(index => new TagDefinition
            {
                Id = $"T{index:D5}",
                Name = $"Tag {index:D5}",
                DeviceId = "SIM01",
                Address = $"A{index}",
                ScanGroup = "Normal"
            })
            .ToList();

        var issues = RuntimeOptionsValidation.CollectIssues(options);

        Assert.DoesNotContain(issues, issue => issue.IsBlocking);
    }

    private static void AssertSchemaRejected(string json, string expectedText)
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "project.json");
            File.WriteAllText(path, json);
            var store = new ProjectConfigurationStore(new ProjectPath(path));

            var exception = Assert.Throws<ProjectDocumentException>(() => store.Load());
            Assert.Contains(expectedText, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static ProjectConfigurationStore CreateStore(string directory) =>
        new(new ProjectPath(Path.Combine(directory, "project.json")));

    private static RuntimeOptions CreateOptions(params string[] tagIds)
    {
        var options = new RuntimeOptions
        {
            Devices =
            [
                new DeviceDefinition
                {
                    Id = "SIM01",
                    Name = "Simulator",
                    DriverType = "Simulator"
                }
            ]
        };
        options.Tags = tagIds.Select((id, index) => new TagDefinition
        {
            Id = id,
            Name = $"Tag {index + 1}",
            DeviceId = "SIM01",
            Address = $"A{index + 1}",
            ScanGroup = "Normal"
        }).ToList();
        return options;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScadaM4", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
