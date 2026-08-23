using Microsoft.Data.Sqlite;

namespace Scada.Infrastructure.History;

internal static class SqliteConnectionConfiguration
{
    internal const int BusyTimeoutMilliseconds = 250;

    internal static Task ConfigureWriteAsync(
        SqliteConnection connection,
        bool enableWal,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            $"{(enableWal ? "PRAGMA journal_mode=WAL;" : string.Empty)} PRAGMA synchronous=NORMAL; PRAGMA busy_timeout={BusyTimeoutMilliseconds};",
            cancellationToken);

    internal static Task ConfigureReadAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        ExecuteAsync(connection, $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};", cancellationToken);

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
