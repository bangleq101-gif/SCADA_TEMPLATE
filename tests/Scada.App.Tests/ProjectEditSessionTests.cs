using System.IO;
using Scada.App.Services;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Tags;
using Scada.Core.MachineSettings;
using Scada.Infrastructure.Persistence;
using Scada.Core.Alarms;
using Xunit;

namespace Scada.App.Tests;

public sealed class ProjectEditSessionTests
{
    [Fact]
    public void WorkingMutationDoesNotMutateStartupOrSavedSnapshots()
    {
        var options = CreateOptions();
        var session = CreateSession(options);

        session.WorkingProject.Tags[0].Address = "Changed";
        session.WorkingProject.Devices[0].ConnectionOptions["Endpoint"] = "new";
        session.MarkChanged();

        Assert.Equal("A1", session.StartupProject.Tags[0].Address);
        Assert.Equal("A1", session.SavedProject.Tags[0].Address);
        Assert.Equal("old", session.StartupProject.Devices[0].ConnectionOptions["Endpoint"]);
        Assert.True(session.IsDirty);
        Assert.True(session.RestartRequired);
    }

    [Fact]
    public void InfluxSettingsAreDeepClonedAndComparedAcrossSnapshots()
    {
        var options = CreateOptions();
        options.Historian.StorageProvider = Scada.Core.History.HistoryStorageProvider.InfluxDb2;
        options.Historian.Influx.Organization = "org";
        options.Historian.Influx.Bucket = "bucket";
        options.Historian.Influx.MaxBufferedSamples = 1234;
        var session = CreateSession(options);

        session.WorkingProject.Historian.Influx.Bucket = "changed";
        session.WorkingProject.Historian.Influx.MaxBufferedSamples = 4321;
        session.MarkChanged();

        Assert.Equal("bucket", session.StartupProject.Historian.Influx.Bucket);
        Assert.Equal("bucket", session.SavedProject.Historian.Influx.Bucket);
        Assert.Equal(1234, session.StartupProject.Historian.Influx.MaxBufferedSamples);
        Assert.True(session.IsDirty);
        Assert.True(session.RestartRequired);
    }

    [Fact]
    public void SaveCreatesIndependentSavedSnapshotWithoutMutatingStartupRuntimeOptions()
    {
        var directory = CreateTempDirectory();
        try
        {
            var options = CreateOptions();
            var session = CreateSession(options, directory);
            session.WorkingProject.Tags[0].Address = "Changed";
            session.MarkChanged();

            Assert.True(session.TrySave());
            Assert.False(session.IsDirty);
            Assert.True(session.RestartRequired);
            Assert.Equal("A1", options.Tags[0].Address);
            Assert.Equal("Changed", session.SavedProject.Tags[0].Address);

            session.WorkingProject.Tags[0].Address = "Changed Again";
            session.MarkChanged();
            Assert.Equal("Changed", session.SavedProject.Tags[0].Address);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void RevertCreatesIndependentWorkingSnapshotFromSaved()
    {
        var directory = CreateTempDirectory();
        try
        {
            var session = CreateSession(CreateOptions(), directory);
            session.WorkingProject.Tags[0].Address = "Changed";
            session.MarkChanged();
            Assert.True(session.TrySave());

            session.WorkingProject.Tags[0].Address = "Unsaved";
            session.MarkChanged();
            session.Revert();

            Assert.Equal("Changed", session.WorkingProject.Tags[0].Address);
            session.WorkingProject.Tags[0].Address = "After Revert";
            Assert.Equal("Changed", session.SavedProject.Tags[0].Address);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SaveWithoutCanonicalPathReportsBlockingIssue()
    {
        var session = CreateSession(CreateOptions());
        session.WorkingProject.Tags[0].Address = "Changed";
        session.MarkChanged();

        Assert.False(session.TrySave());
        Assert.Contains(session.ValidationIssues, issue => issue.Code == "PROJECT_PATH_REQUIRED");
    }

    [Fact]
    public void MachineSettingsAreDeepClonedSavedAndRestoredByProjectRevert()
    {
        var directory = CreateTempDirectory();
        try
        {
            var options = CreateOptions();
            options.MachineSettings.Pages = [new MachineSettingsPageDefinition { Id = "page", Title = "Page", Group = "Drive", Parameters = [new MachineParameterDefinition { Id = "speed", Name = "Speed", Group = "Limits", ValueType = MachineParameterValueType.Integer, Value = "10", Min = 0, Max = 20, Unit = "rpm" }] }];
            var session = CreateSession(options, directory);
            session.WorkingProject.MachineSettings.Pages[0].Parameters[0].Value = "12";
            session.MarkChanged();

            Assert.True(session.TrySave());
            Assert.True(session.RestartRequired);
            Assert.Equal("10", session.StartupProject.MachineSettings.Pages[0].Parameters[0].Value);
            session.WorkingProject.MachineSettings.Pages[0].Group = "Changed";
            session.WorkingProject.MachineSettings.Pages[0].Parameters[0].Group = "Changed";
            session.MarkChanged();
            session.Revert();

            Assert.Equal("Drive", session.WorkingProject.MachineSettings.Pages[0].Group);
            Assert.Equal("Limits", session.WorkingProject.MachineSettings.Pages[0].Parameters[0].Group);
            Assert.Equal("12", session.WorkingProject.MachineSettings.Pages[0].Parameters[0].Value);
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public void AlarmSettingsAreDeepClonedComparedSavedAndReverted()
    {
        var directory = CreateTempDirectory();
        try
        {
            var options = CreateOptions();
            options.Alarms.Enabled = true;
            options.Alarms.Definitions =
            [
                new AlarmDefinition
                {
                    Id = "A1", Name = "High", TagId = "T1", RuleType = AlarmRuleType.High,
                    Severity = AlarmSeverity.High, Threshold = 10, Deadband = 1,
                    ActivationDelay = TimeSpan.FromMilliseconds(250)
                }
            ];
            options.Tags[0].DataType = TagDataType.Double;
            var session = CreateSession(options, directory);

            session.WorkingProject.Alarms.Definitions[0].Threshold = 12;
            session.MarkChanged();

            Assert.True(session.IsDirty);
            Assert.True(session.RestartRequired);
            Assert.Equal(10, session.StartupProject.Alarms.Definitions[0].Threshold);
            Assert.Equal(10, session.SavedProject.Alarms.Definitions[0].Threshold);
            Assert.True(session.TrySave());
            Assert.Equal(12, session.SavedProject.Alarms.Definitions[0].Threshold);

            session.WorkingProject.Alarms.Definitions[0].Message = "Unsaved";
            session.MarkChanged();
            session.Revert();

            Assert.Equal(string.Empty, session.WorkingProject.Alarms.Definitions[0].Message);
            Assert.Equal(6, new ProjectConfigurationStore(new ProjectPath(Path.Combine(directory, "project.json"))).Load()!.SchemaVersion);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static ProjectEditSession CreateSession(RuntimeOptions options, string? directory = null)
    {
        if (directory is null)
        {
            return new ProjectEditSession(options, null, null);
        }

        var path = new ProjectPath(Path.Combine(directory, "project.json"));
        return new ProjectEditSession(options, path, new ProjectConfigurationStore(path));
    }

    private static RuntimeOptions CreateOptions() => new()
    {
        Devices =
        [
            new DeviceDefinition
            {
                Id = "SIM01",
                Name = "Simulator",
                DriverType = "Simulator",
                ConnectionOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Endpoint"] = "old"
                }
            }
        ],
        Tags =
        [
            new TagDefinition
            {
                Id = "T1",
                Name = "Tag 1",
                DeviceId = "SIM01",
                Address = "A1"
            }
        ]
    };

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
