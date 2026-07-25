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
        await ExecuteAsync(connection, $"PRAGMA user_version = {CurrentSchemaVersion};", cancellationToken).ConfigureAwait(false);

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

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS speed_tests (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                direction TEXT NOT NULL,
                succeeded INTEGER NOT NULL,
                mbps REAL NULL,
                bytes_transferred INTEGER NOT NULL,
                active_duration_ms REAL NOT NULL,
                provider TEXT NOT NULL,
                endpoint TEXT NULL,
                failure_category TEXT NULL,
                failure_message TEXT NULL,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS incidents (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                started_at TEXT NOT NULL,
                ended_at TEXT NULL,
                severity TEXT NOT NULL,
                classification TEXT NOT NULL,
                explanation TEXT NOT NULL,
                confidence TEXT NOT NULL,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS manual_markers (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                note TEXT NOT NULL,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS network_interface_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                event_type TEXT NOT NULL,
                interface_name TEXT NULL,
                gateway TEXT NULL,
                details TEXT NULL,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            """,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
