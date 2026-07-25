using DQOPR.NetPulse.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DQOPR.NetPulse.Storage.Tests;

public sealed class SchemaBootstrapperTests
{
    [Fact]
    public async Task CreatesInitialDurableTables()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netpulse-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();

        try
        {
            await SchemaBootstrapper.InitializeAsync(connectionString, CancellationToken.None);

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('sessions', 'measurements', 'speed_tests', 'incidents', 'manual_markers', 'network_interface_events', 'reference_speed_results') ORDER BY name;";
            var names = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            while (await reader.ReadAsync(CancellationToken.None))
            {
                names.Add(reader.GetString(0));
            }

            Assert.Equal(["incidents", "manual_markers", "measurements", "network_interface_events", "reference_speed_results", "sessions", "speed_tests"], names);

            Assert.Contains("methodology_version", await ColumnsAsync(connection, "sessions"));
            Assert.Contains("probe_stream_id", await ColumnsAsync(connection, "measurements"));
            Assert.Contains("result_status", await ColumnsAsync(connection, "speed_tests"));
            Assert.Contains("diagnostic_json", await ColumnsAsync(connection, "speed_tests"));

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            Assert.Equal(SchemaBootstrapper.CurrentSchemaVersion, Convert.ToInt32(await versionCommand.ExecuteScalarAsync(CancellationToken.None)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }

    private static async Task<IReadOnlyList<string>> ColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}
