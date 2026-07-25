using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Core.Storage;
using DQOPR.NetPulse.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DQOPR.NetPulse.Storage.Repositories;

public sealed class SqliteNetPulseStore(string connectionString) : INetPulseStore
{
    private readonly string connectionString = connectionString;

    public Task InitializeAsync(CancellationToken cancellationToken)
        => SchemaBootstrapper.InitializeAsync(connectionString, cancellationToken);

    public async Task CreateSessionAsync(MonitoringSession session, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO sessions (id, started_at, ended_at, profile_name, active_duration_seconds, paused_duration_seconds, status, methodology_version)
            VALUES ($id, $started_at, $ended_at, $profile_name, $active_duration_seconds, $paused_duration_seconds, $status, $methodology_version);
            """;
        await ExecuteAsync(sql, AddSessionParameters(session), cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSessionAsync(MonitoringSession session, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE sessions
            SET ended_at = $ended_at,
                active_duration_seconds = $active_duration_seconds,
                paused_duration_seconds = $paused_duration_seconds,
                status = $status,
                methodology_version = $methodology_version
            WHERE id = $id;
            """;
        await ExecuteAsync(sql, AddSessionParameters(session), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MonitoringSession>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        var sessions = new List<MonitoringSession>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, started_at, ended_at, profile_name, active_duration_seconds, paused_duration_seconds, status, methodology_version
            FROM sessions
            ORDER BY started_at DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions.Add(new MonitoringSession(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetString(3),
                TimeSpan.FromSeconds(reader.GetInt64(4)),
                TimeSpan.FromSeconds(reader.GetInt64(5)),
                Enum.Parse<SessionStatus>(reader.GetString(6)),
                reader.GetString(7)));
        }

        return sessions;
    }

    public async Task MarkRunningSessionsInterruptedAsync(DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE sessions
            SET status = $status, ended_at = COALESCE(ended_at, $ended_at)
            WHERE status IN ('Running', 'Paused', 'Created');
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("$status", SessionStatus.Interrupted.ToString());
                command.Parameters.AddWithValue("$ended_at", observedAt.ToString("O"));
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveMeasurementAsync(ProbeMeasurement measurement, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO measurements (session_id, observed_at, method, target_name, succeeded, latency_ms, failure_category, failure_message, target_host, address_family, probe_stream_id, sequence, methodology_version)
            VALUES ($session_id, $observed_at, $method, $target_name, $succeeded, $latency_ms, $failure_category, $failure_message, $target_host, $address_family, $probe_stream_id, $sequence, $methodology_version);
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("$session_id", measurement.SessionId.ToString());
                command.Parameters.AddWithValue("$observed_at", measurement.ObservedAt.ToString("O"));
                command.Parameters.AddWithValue("$method", measurement.Method.ToString());
                command.Parameters.AddWithValue("$target_name", measurement.TargetName);
                command.Parameters.AddWithValue("$succeeded", measurement.Succeeded ? 1 : 0);
                command.Parameters.AddWithValue("$latency_ms", (object?)measurement.LatencyMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue("$failure_category", (object?)measurement.FailureCategory ?? DBNull.Value);
                command.Parameters.AddWithValue("$failure_message", (object?)measurement.FailureMessage ?? DBNull.Value);
                command.Parameters.AddWithValue("$target_host", (object?)measurement.TargetHost ?? DBNull.Value);
                command.Parameters.AddWithValue("$address_family", (object?)measurement.AddressFamily ?? DBNull.Value);
                command.Parameters.AddWithValue("$probe_stream_id", (object?)measurement.ProbeStreamId ?? DBNull.Value);
                command.Parameters.AddWithValue("$sequence", (object?)measurement.Sequence ?? DBNull.Value);
                command.Parameters.AddWithValue("$methodology_version", measurement.MethodologyVersion);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveSpeedTestAsync(SpeedTestMeasurement measurement, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO speed_tests (session_id, observed_at, direction, succeeded, mbps, bytes_transferred, active_duration_ms, provider, endpoint, failure_category, failure_message, result_status, setup_duration_ms, transfer_duration_ms, warmup_duration_ms, parallel_stream_count, http_version, methodology_version, diagnostic_json)
            VALUES ($session_id, $observed_at, $direction, $succeeded, $mbps, $bytes_transferred, $active_duration_ms, $provider, $endpoint, $failure_category, $failure_message, $result_status, $setup_duration_ms, $transfer_duration_ms, $warmup_duration_ms, $parallel_stream_count, $http_version, $methodology_version, $diagnostic_json);
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("$session_id", measurement.SessionId.ToString());
                command.Parameters.AddWithValue("$observed_at", measurement.ObservedAt.ToString("O"));
                command.Parameters.AddWithValue("$direction", measurement.Direction);
                command.Parameters.AddWithValue("$succeeded", measurement.Succeeded ? 1 : 0);
                command.Parameters.AddWithValue("$mbps", (object?)measurement.MegabitsPerSecond ?? DBNull.Value);
                command.Parameters.AddWithValue("$bytes_transferred", measurement.BytesTransferred);
                command.Parameters.AddWithValue("$active_duration_ms", measurement.ActiveDuration.TotalMilliseconds);
                command.Parameters.AddWithValue("$provider", measurement.Provider);
                command.Parameters.AddWithValue("$endpoint", (object?)measurement.Endpoint ?? DBNull.Value);
                command.Parameters.AddWithValue("$failure_category", (object?)measurement.FailureCategory ?? DBNull.Value);
                command.Parameters.AddWithValue("$failure_message", (object?)measurement.FailureMessage ?? DBNull.Value);
                command.Parameters.AddWithValue("$result_status", measurement.ResultStatus);
                command.Parameters.AddWithValue("$setup_duration_ms", (object?)measurement.SetupDuration?.TotalMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue("$transfer_duration_ms", (object?)measurement.TransferDuration?.TotalMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue("$warmup_duration_ms", (object?)measurement.WarmupDuration?.TotalMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue("$parallel_stream_count", measurement.ParallelStreamCount);
                command.Parameters.AddWithValue("$http_version", (object?)measurement.HttpVersion ?? DBNull.Value);
                command.Parameters.AddWithValue("$methodology_version", measurement.MethodologyVersion);
                command.Parameters.AddWithValue("$diagnostic_json", (object?)measurement.DiagnosticJson ?? DBNull.Value);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveNetworkInterfaceEventAsync(NetworkInterfaceEvent networkEvent, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO network_interface_events (session_id, observed_at, event_type, interface_name, gateway, details)
            VALUES ($session_id, $observed_at, $event_type, $interface_name, $gateway, $details);
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("$session_id", networkEvent.SessionId.ToString());
                command.Parameters.AddWithValue("$observed_at", networkEvent.ObservedAt.ToString("O"));
                command.Parameters.AddWithValue("$event_type", networkEvent.EventType);
                command.Parameters.AddWithValue("$interface_name", (object?)networkEvent.InterfaceName ?? DBNull.Value);
                command.Parameters.AddWithValue("$gateway", (object?)networkEvent.Gateway ?? DBNull.Value);
                command.Parameters.AddWithValue("$details", (object?)networkEvent.Details ?? DBNull.Value);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveManualMarkerAsync(ManualMarker marker, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO manual_markers (id, session_id, observed_at, note)
            VALUES ($id, $session_id, $observed_at, $note);
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("$id", marker.Id.ToString());
                command.Parameters.AddWithValue("$session_id", marker.SessionId.ToString());
                command.Parameters.AddWithValue("$observed_at", marker.ObservedAt.ToString("O"));
                command.Parameters.AddWithValue("$note", marker.Note);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveReferenceSpeedResultAsync(ReferenceSpeedResult result, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO reference_speed_results (id, session_id, observed_at, provider, download_mbps, upload_mbps, latency_ms, notes)
            VALUES ($id, $session_id, $observed_at, $provider, $download_mbps, $upload_mbps, $latency_ms, $notes);
            """;
        await ExecuteAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("$id", result.Id.ToString());
                command.Parameters.AddWithValue("$session_id", (object?)result.SessionId?.ToString() ?? DBNull.Value);
                command.Parameters.AddWithValue("$observed_at", result.ObservedAt.ToString("O"));
                command.Parameters.AddWithValue("$provider", result.Provider);
                command.Parameters.AddWithValue("$download_mbps", (object?)result.DownloadMegabitsPerSecond ?? DBNull.Value);
                command.Parameters.AddWithValue("$upload_mbps", (object?)result.UploadMegabitsPerSecond ?? DBNull.Value);
                command.Parameters.AddWithValue("$latency_ms", (object?)result.LatencyMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue("$notes", (object?)result.Notes ?? DBNull.Value);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProbeMeasurement>> GetMeasurementsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var measurements = new List<ProbeMeasurement>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT observed_at, method, target_name, succeeded, latency_ms, failure_category, failure_message, target_host, address_family, probe_stream_id, sequence, methodology_version
            FROM measurements
            WHERE session_id = $session_id
            ORDER BY observed_at, id;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            measurements.Add(new ProbeMeasurement(
                sessionId,
                DateTimeOffset.Parse(reader.GetString(0)),
                Enum.Parse<ProbeMethod>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt64(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.GetString(11)));
        }

        return measurements;
    }

    public async Task<IReadOnlyList<SpeedTestMeasurement>> GetSpeedTestsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var speedTests = new List<SpeedTestMeasurement>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT observed_at, direction, succeeded, mbps, bytes_transferred, active_duration_ms, provider, endpoint, failure_category, failure_message, result_status, setup_duration_ms, transfer_duration_ms, warmup_duration_ms, parallel_stream_count, http_version, methodology_version, diagnostic_json
            FROM speed_tests
            WHERE session_id = $session_id
            ORDER BY observed_at, id;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            speedTests.Add(new SpeedTestMeasurement(
                sessionId,
                DateTimeOffset.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt64(2) == 1,
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.GetInt64(4),
                TimeSpan.FromMilliseconds(reader.GetDouble(5)),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : TimeSpan.FromMilliseconds(reader.GetDouble(11)),
                reader.IsDBNull(12) ? null : TimeSpan.FromMilliseconds(reader.GetDouble(12)),
                reader.IsDBNull(13) ? null : TimeSpan.FromMilliseconds(reader.GetDouble(13)),
                reader.GetInt32(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17)));
        }

        return speedTests;
    }

    public async Task<IReadOnlyList<NetworkInterfaceEvent>> GetNetworkInterfaceEventsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var events = new List<NetworkInterfaceEvent>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT observed_at, event_type, interface_name, gateway, details
            FROM network_interface_events
            WHERE session_id = $session_id
            ORDER BY observed_at, id;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(new NetworkInterfaceEvent(
                sessionId,
                DateTimeOffset.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return events;
    }

    public async Task<IReadOnlyList<ManualMarker>> GetManualMarkersAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var markers = new List<ManualMarker>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, observed_at, note
            FROM manual_markers
            WHERE session_id = $session_id
            ORDER BY observed_at;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            markers.Add(new ManualMarker(
                Guid.Parse(reader.GetString(0)),
                sessionId,
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2)));
        }

        return markers;
    }

    public async Task<IReadOnlyList<ReferenceSpeedResult>> GetReferenceSpeedResultsAsync(Guid? sessionId, CancellationToken cancellationToken)
    {
        var results = new List<ReferenceSpeedResult>();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sessionId is null
            ? """
              SELECT id, session_id, observed_at, provider, download_mbps, upload_mbps, latency_ms, notes
              FROM reference_speed_results
              ORDER BY observed_at DESC;
              """
            : """
              SELECT id, session_id, observed_at, provider, download_mbps, upload_mbps, latency_ms, notes
              FROM reference_speed_results
              WHERE session_id = $session_id
              ORDER BY observed_at DESC;
              """;
        if (sessionId is not null)
        {
            command.Parameters.AddWithValue("$session_id", sessionId.Value.ToString());
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ReferenceSpeedResult(
                Guid.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return results;
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var table in new[] { "measurements", "speed_tests", "incidents", "manual_markers", "network_interface_events", "reference_speed_results", "sessions" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = $"DELETE FROM {table} WHERE {(table == "sessions" ? "id" : "session_id")} = $session_id;";
            command.Parameters.AddWithValue("$session_id", sessionId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        await foreignKeys.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task ExecuteAsync(string sql, Action<SqliteCommand> bind, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind(command);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Action<SqliteCommand> AddSessionParameters(MonitoringSession session)
    {
        return command =>
        {
            command.Parameters.AddWithValue("$id", session.Id.ToString());
            command.Parameters.AddWithValue("$started_at", session.StartedAt.ToString("O"));
            command.Parameters.AddWithValue("$ended_at", (object?)session.EndedAt?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$profile_name", session.ProfileName);
            command.Parameters.AddWithValue("$active_duration_seconds", (long)session.ActiveDuration.TotalSeconds);
            command.Parameters.AddWithValue("$paused_duration_seconds", (long)session.PausedDuration.TotalSeconds);
            command.Parameters.AddWithValue("$status", session.Status.ToString());
            command.Parameters.AddWithValue("$methodology_version", session.MethodologyVersion);
        };
    }
}
