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
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('sessions', 'measurements') ORDER BY name;";
            var names = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            while (await reader.ReadAsync(CancellationToken.None))
            {
                names.Add(reader.GetString(0));
            }

            Assert.Equal(["measurements", "sessions"], names);
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
}
