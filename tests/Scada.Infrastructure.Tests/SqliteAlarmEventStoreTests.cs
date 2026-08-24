using Microsoft.Data.Sqlite;
using Scada.Core.Alarms;
using Scada.Core.Tags;
using Scada.Infrastructure.Alarms;
using Scada.Infrastructure.Persistence;
using Xunit;

namespace Scada.Infrastructure.Tests;

[Collection(SqliteTestCollection.Name)]
public sealed class SqliteAlarmEventStoreTests
{
    [Fact]
    public void PathResolverRequiresCanonicalProjectAndRejectsAbsoluteOrTraversal()
    {
        var directory = CreateTempDirectory();
        try
        {
            var project = new ProjectPath(Path.Combine(directory, "project.json"));
            Assert.Equal(
                Path.Combine(directory, "Data", "alarms.db"),
                AlarmDatabasePathResolver.Resolve(project, "Data/alarms.db"));
            Assert.Equal("PROJECT_PATH_REQUIRED", Assert.Throws<AlarmStoreException>(
                () => AlarmDatabasePathResolver.Resolve(null, "Data/alarms.db")).Code);
            Assert.Equal("ALARM_DATABASE_PATH_INVALID", Assert.Throws<AlarmStoreException>(
                () => AlarmDatabasePathResolver.Resolve(project, Path.Combine(directory, "alarms.db"))).Code);
            Assert.Equal("ALARM_DATABASE_PATH_OUTSIDE_PROJECT", Assert.Throws<AlarmStoreException>(
                () => AlarmDatabasePathResolver.Resolve(project, "../alarms.db")).Code);
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task UntrustedStartupMarkerAndTrustedCheckpointRoundTripAtomically()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            await store.InitializeAsync(CancellationToken.None);
            var session = Guid.NewGuid();
            await store.BeginUntrustedSessionAsync(
                new AlarmStoreSessionRequest(session, "Runtime01", Utc(0)), CancellationToken.None);

            var afterMarker = await store.LoadRecoveryAsync(CancellationToken.None);
            Assert.False(afterMarker.RecoveryTrusted);
            Assert.Empty(afterMarker.OpenInstances);

            var instance = Instance("A1", Guid.NewGuid(), AlarmLifecycleState.ActiveAcknowledged);
            var alarmEvent = Event(instance, 1, AlarmEventType.Activated);
            await store.PersistBatchAsync(
                new AlarmPersistenceBatch(session, [alarmEvent], [instance], 1), CancellationToken.None);
            await store.CommitTrustedCheckpointAsync(
                new AlarmStoreCheckpoint(session, true, 1, Utc(3), [instance]), CancellationToken.None);

            var recovered = await CreateStore(directory).LoadRecoveryAsync(CancellationToken.None);
            Assert.True(recovered.RecoveryTrusted);
            Assert.Equal(1, recovered.ContinuitySequence);
            Assert.Equal(instance, Assert.Single(recovered.OpenInstances));
            Assert.Equal(alarmEvent, Assert.Single(await store.QueryAsync(
                new AlarmEventQuery(Utc(-1), Utc(10)), CancellationToken.None)));
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task BeginningNextSessionDurablyInvalidatesOlderTrustedCheckpoint()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            await store.InitializeAsync(CancellationToken.None);
            var first = Guid.NewGuid();
            var instance = Instance("A1", Guid.NewGuid(), AlarmLifecycleState.ActiveUnacknowledged);
            await store.BeginUntrustedSessionAsync(new(first, "Runtime01", Utc(0)), CancellationToken.None);
            await store.CommitTrustedCheckpointAsync(new(first, true, 0, Utc(1), [instance]), CancellationToken.None);

            await store.BeginUntrustedSessionAsync(new(Guid.NewGuid(), "Runtime01", Utc(2)), CancellationToken.None);
            var recovery = await CreateStore(directory).LoadRecoveryAsync(CancellationToken.None);

            Assert.False(recovery.RecoveryTrusted);
            Assert.Equal(instance, Assert.Single(recovery.OpenInstances));
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task NewerOrCorruptSchemaIsRejected()
    {
        var newerDirectory = CreateTempDirectory();
        var corruptDirectory = CreateTempDirectory();
        try
        {
            var newerPath = Path.Combine(newerDirectory, "Data", "alarms.db");
            Directory.CreateDirectory(Path.GetDirectoryName(newerPath)!);
            await using (var connection = new SqliteConnection($"Data Source={newerPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version=99;";
                await command.ExecuteNonQueryAsync();
            }
            var newer = await Assert.ThrowsAsync<AlarmStoreException>(
                () => CreateStore(newerDirectory).InitializeAsync(CancellationToken.None));
            Assert.Equal("ALARM_SQLITE_SCHEMA_TOO_NEW", newer.Code);

            var corruptPath = Path.Combine(corruptDirectory, "Data", "alarms.db");
            Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
            await File.WriteAllTextAsync(corruptPath, "not sqlite");
            var corrupt = await Assert.ThrowsAsync<AlarmStoreException>(
                () => CreateStore(corruptDirectory).InitializeAsync(CancellationToken.None));
            Assert.Equal("ALARM_SQLITE_CORRUPT", corrupt.Code);
        }
        finally
        {
            DeleteDirectory(newerDirectory);
            DeleteDirectory(corruptDirectory);
        }
    }

    private static SqliteAlarmEventStore CreateStore(string directory) =>
        new(new ProjectPath(Path.Combine(directory, "project.json")), "Data/alarms.db");

    private static AlarmInstanceRecord Instance(string alarmId, Guid instanceId, AlarmLifecycleState state) =>
        new(alarmId, instanceId, state, AlarmSeverity.High, "FP", Utc(1), Utc(2), "operator", 10, Utc(1), TagQuality.Good);

    private static AlarmEvent Event(AlarmInstanceRecord instance, long sequence, AlarmEventType type) =>
        new(sequence, instance.AlarmId, instance.InstanceId, type, instance.Severity, Utc(2), instance.DefinitionFingerprint, 10, Utc(1), "operator");

    private static DateTimeOffset Utc(int seconds) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScadaM11", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
