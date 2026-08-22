using System.IO;
using Scada.App.Services;
using Scada.Core.Configuration;
using Scada.Core.Devices;
using Scada.Core.Tags;
using Scada.Infrastructure.Persistence;
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
