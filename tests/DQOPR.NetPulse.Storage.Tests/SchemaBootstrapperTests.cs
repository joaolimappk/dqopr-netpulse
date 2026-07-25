using DQOPR.NetPulse.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DQOPR.NetPulse.Storage.Tests;

public sealed class SchemaBootstrapperTests
{
    [Fact]
    public async Task CreatesInitialDurableTables()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netpulse-{Guid.NewGuid():N}.db");

        try
        {
            await SchemaBootstrapper.InitializeAsync(new SqliteConnectionStringBuilder { DataSource = path }.ToString(), CancellationToken.None);

            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
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
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
