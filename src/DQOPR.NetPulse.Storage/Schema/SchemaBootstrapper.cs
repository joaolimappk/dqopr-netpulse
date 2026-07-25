using Microsoft.Data.Sqlite;

namespace DQOPR.NetPulse.Storage.Schema;

public static class SchemaBootstrapper
{
    public const int CurrentSchemaVersion = 1;

    public static async Task InitializeAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA user_version = 1;", cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                started_at TEXT NOT NULL,
                ended_at TEXT NULL,
                profile_name TEXT NOT NULL,
                active_duration_seconds INTEGER NOT NULL DEFAULT 0,
                paused_duration_seconds INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS measurements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                method TEXT NOT NULL,
                target_name TEXT NOT NULL,
                succeeded INTEGER NOT NULL,
                latency_ms REAL NULL,
                failure_category TEXT NULL,
                failure_message TEXT NULL,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            "CREATE INDEX IF NOT EXISTS idx_measurements_session_time ON measurements(session_id, observed_at);",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
