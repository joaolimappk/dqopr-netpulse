using Microsoft.Data.Sqlite;

namespace DQOPR.NetPulse.Storage.Schema;

public static class SchemaBootstrapper
{
    public const int CurrentSchemaVersion = 2;

    public static async Task InitializeAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);

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
                status TEXT NOT NULL,
                methodology_version TEXT NOT NULL DEFAULT 'alpha.4'
            );
            """,
            cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "sessions", "methodology_version", "TEXT NOT NULL DEFAULT 'pre-alpha.4'", cancellationToken).ConfigureAwait(false);

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
                target_host TEXT NULL,
                address_family TEXT NULL,
                probe_stream_id TEXT NULL,
                sequence INTEGER NULL,
                methodology_version TEXT NOT NULL DEFAULT 'alpha.4',
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            """,
            cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "measurements", "target_host", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "measurements", "address_family", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "measurements", "probe_stream_id", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "measurements", "sequence", "INTEGER NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "measurements", "methodology_version", "TEXT NOT NULL DEFAULT 'pre-alpha.4'", cancellationToken).ConfigureAwait(false);

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
                result_status TEXT NOT NULL DEFAULT 'Valid',
                setup_duration_ms REAL NULL,
                transfer_duration_ms REAL NULL,
                warmup_duration_ms REAL NULL,
                parallel_stream_count INTEGER NOT NULL DEFAULT 1,
                http_version TEXT NULL,
                methodology_version TEXT NOT NULL DEFAULT 'alpha.4',
                diagnostic_json TEXT NULL,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            """,
            cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "speed_tests", "result_status", "TEXT NOT NULL DEFAULT 'Legacy estimate - methodology version prior to alpha.4'", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "speed_tests", "setup_duration_ms", "REAL NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "speed_tests", "transfer_duration_ms", "REAL NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "speed_tests", "warmup_duration_ms", "REAL NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "speed_tests", "parallel_stream_count", "INTEGER NOT NULL DEFAULT 1", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "speed_tests", "http_version", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "speed_tests", "methodology_version", "TEXT NOT NULL DEFAULT 'pre-alpha.4'", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "speed_tests", "diagnostic_json", "TEXT NULL", cancellationToken).ConfigureAwait(false);

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

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS reference_speed_results (
                id TEXT PRIMARY KEY,
                session_id TEXT NULL,
                observed_at TEXT NOT NULL,
                provider TEXT NOT NULL,
                download_mbps REAL NULL,
                upload_mbps REAL NULL,
                latency_ms REAL NULL,
                notes TEXT NULL,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"PRAGMA user_version = {CurrentSchemaVersion};", cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        await using var probe = connection.CreateCommand();
        probe.CommandText = $"PRAGMA table_info({table});";
        await using (var reader = await probe.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await ExecuteAsync(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};", cancellationToken).ConfigureAwait(false);
    }
}
