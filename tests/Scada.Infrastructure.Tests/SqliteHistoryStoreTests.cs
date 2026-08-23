using Microsoft.Data.Sqlite;
using Scada.Core.History;
using Scada.Core.Tags;
using Scada.Infrastructure.History;
using Scada.Infrastructure.Persistence;
using Xunit;

namespace Scada.Infrastructure.Tests;

[Collection(SqliteTestCollection.Name)]
public sealed class SqliteHistoryStoreTests
{
    [Fact]
    public async Task PathResolverRejectsAbsoluteAndTraversalPaths()
    {
        var directory = CreateTempDirectory();
        try
        {
            var projectPath = new ProjectPath(Path.Combine(directory, "project.json"));

            var absolute = new SqliteHistoryStore(projectPath, Path.Combine(directory, "history.db"));
            var traversal = new SqliteHistoryStore(projectPath, "..\\outside.db");

            Assert.Equal("HISTORIAN_DATABASE_PATH_INVALID", (await absolute.PreflightAsync(CancellationToken.None)).ErrorCode);
            Assert.Equal("HISTORIAN_DATABASE_PATH_OUTSIDE_PROJECT", (await traversal.PreflightAsync(CancellationToken.None)).ErrorCode);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task TypedValuesQualityAndEqualTimestampOrderRoundTrip()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            Assert.Equal(HistoryStorePreflightStatus.Ready, (await store.PreflightAsync(CancellationToken.None)).Status);
            await store.InitializeAsync(CancellationToken.None);

            var source = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var recorded = DateTimeOffset.Parse("2026-01-01T00:00:01Z");
            await store.WriteBatchAsync(
            [
                Sample("T1", TagDataType.Double, 1.25d, TagQuality.Good, source, recorded, 1),
                Sample("T1", TagDataType.Double, 2.5d, TagQuality.Disconnected, source, recorded, 2),
                Sample("T2", TagDataType.Boolean, true, TagQuality.Good, source, recorded.AddSeconds(1), 3),
                Sample("T3", TagDataType.Int32, 42, TagQuality.Good, source, recorded.AddSeconds(1), 4),
                Sample("T4", TagDataType.Int64, long.MaxValue, TagQuality.Good, source, recorded.AddSeconds(1), 5),
                Sample("T5", TagDataType.String, "hello", TagQuality.Good, source, recorded.AddSeconds(1), 6),
                Sample("T6", TagDataType.Double, null, TagQuality.Bad, source, recorded.AddSeconds(1), 7)
            ], CancellationToken.None);

            var query = new HistoryQuery("Runtime01", "T1", recorded.AddSeconds(-1), recorded.AddSeconds(2), 10);
            var ordered = await store.QueryAsync(query, CancellationToken.None);
            var boolean = await store.QueryAsync(query with { TagId = "T2" }, CancellationToken.None);
            var int32 = await store.QueryAsync(query with { TagId = "T3" }, CancellationToken.None);
            var int64 = await store.QueryAsync(query with { TagId = "T4" }, CancellationToken.None);
            var text = await store.QueryAsync(query with { TagId = "T5" }, CancellationToken.None);
            var nullValue = await store.QueryAsync(query with { TagId = "T6" }, CancellationToken.None);

            Assert.Collection(
                ordered,
                first =>
                {
                    Assert.Equal(1.25d, first.Value);
                    Assert.Equal(TagQuality.Good, first.Quality);
                },
                second =>
                {
                    Assert.Equal(2.5d, second.Value);
                    Assert.Equal(TagQuality.Disconnected, second.Quality);
                });
            Assert.Equal(true, Assert.Single(boolean).Value);
            Assert.Equal(42, Assert.Single(int32).Value);
            Assert.Equal(long.MaxValue, Assert.Single(int64).Value);
            Assert.Equal("hello", Assert.Single(text).Value);
            var nullSample = Assert.Single(nullValue);
            Assert.Null(nullSample.Value);
            Assert.Equal(TagQuality.Bad, nullSample.Quality);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task WriteConnectionConfigurationAppliesFiniteBusyTimeoutAndNormalSynchronousMode()
    {
        var directory = CreateTempDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "Data", "history.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            await connection.OpenAsync();

            await SqliteConnectionConfiguration.ConfigureWriteAsync(
                connection,
                enableWal: false,
                cancellationToken: CancellationToken.None);

            Assert.Equal(1L, await ReadPragmaAsync(connection, "synchronous"));
            Assert.Equal(SqliteConnectionConfiguration.BusyTimeoutMilliseconds,
                await ReadPragmaAsync(connection, "busy_timeout"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task SchemaUsesUserVersionOneAndDoesNotUseAutoincrement()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            await store.InitializeAsync(CancellationToken.None);
            Assert.DoesNotContain("AUTOINCREMENT", SqliteHistorySchema.CreateTableSql, StringComparison.OrdinalIgnoreCase);

            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = store.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version;";
                Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
            }
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task NewerSchemaIsFaultedBeforeRuntimeWrites()
    {
        var directory = CreateTempDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "Data", "history.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = databasePath,
                       Mode = SqliteOpenMode.ReadWriteCreate
                   }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version=99;";
                command.ExecuteNonQuery();
            }

            var result = await CreateStore(directory).PreflightAsync(CancellationToken.None);

            Assert.Equal(HistoryStorePreflightStatus.Faulted, result.Status);
            Assert.Equal("HISTORIAN_SQLITE_SCHEMA_TOO_NEW", result.ErrorCode);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task CorruptDatabaseIsFaulted()
    {
        var directory = CreateTempDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "Data", "history.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            File.WriteAllText(databasePath, "not a sqlite database");

            var result = await CreateStore(directory).PreflightAsync(CancellationToken.None);

            Assert.Equal(HistoryStorePreflightStatus.Faulted, result.Status);
            Assert.Equal("HISTORIAN_SQLITE_CORRUPT", result.ErrorCode);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task CorruptDatabaseIsTranslatedOutsidePreflight()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            await store.InitializeAsync(CancellationToken.None);
            var databasePath = store.DatabasePath!;

            SqliteConnection.ClearAllPools();
            File.WriteAllText(databasePath, "not a sqlite database");

            var initializeException = await Assert.ThrowsAsync<HistoryStorePermanentException>(
                () => CreateStore(directory).InitializeAsync(CancellationToken.None));
            var writeException = await Assert.ThrowsAsync<HistoryStorePermanentException>(
                () => store.WriteBatchAsync(
                    [Sample(
                        "T1",
                        TagDataType.Double,
                        1d,
                        TagQuality.Good,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        1)],
                    CancellationToken.None));
            var queryException = await Assert.ThrowsAsync<HistoryStorePermanentException>(
                () => store.QueryAsync(
                    new HistoryQuery(
                        "Runtime01",
                        "T1",
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        DateTimeOffset.UtcNow.AddMinutes(1),
                        10),
                    CancellationToken.None));

            Assert.Equal("HISTORIAN_SQLITE_CORRUPT", initializeException.Code);
            Assert.Equal("HISTORIAN_SQLITE_CORRUPT", writeException.Code);
            Assert.Equal("HISTORIAN_SQLITE_CORRUPT", queryException.Code);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ExistingMalformedHistoryTableIsFaulted()
    {
        var directory = CreateTempDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "Data", "history.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = databasePath,
                       Mode = SqliteOpenMode.ReadWriteCreate
                   }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE HistorySamples (Id INTEGER PRIMARY KEY); PRAGMA user_version=1;";
                command.ExecuteNonQuery();
            }

            var result = await CreateStore(directory).PreflightAsync(CancellationToken.None);

            Assert.Equal(HistoryStorePreflightStatus.Faulted, result.Status);
            Assert.Equal("HISTORIAN_SQLITE_SCHEMA_INVALID", result.ErrorCode);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task MissingCanonicalProjectPathIsPermanentFault()
    {
        var result = await new SqliteHistoryStore(null, "Data/history.db")
            .PreflightAsync(CancellationToken.None);

        Assert.Equal(HistoryStorePreflightStatus.Faulted, result.Status);
        Assert.Equal("PROJECT_PATH_REQUIRED", result.ErrorCode);
    }

    private static SqliteHistoryStore CreateStore(string directory) =>
        new(new ProjectPath(Path.Combine(directory, "project.json")), "Data/history.db");

    private static HistorySample Sample(
        string tagId,
        TagDataType dataType,
        object? value,
        TagQuality quality,
        DateTimeOffset source,
        DateTimeOffset recorded,
        long sequence) => new(
        "Runtime01", tagId, dataType, value, quality, source, recorded, sequence);

    private static async Task<long> ReadPragmaAsync(SqliteConnection connection, string pragma)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ScadaM5", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
