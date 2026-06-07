using Microsoft.Data.Sqlite;

namespace ReperioNet.Tests;

/// <summary>A fresh, isolated database path per test, deleted on dispose.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _directory;

    public TestDatabase()
    {
        _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "reperionet-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "index.db");
    }

    public string Path { get; }

    /// <summary>Opens an unpooled raw connection for inspecting the database file.</summary>
    public SqliteConnection OpenRaw()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    public long QueryScalarLong(string sql)
    {
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Returns the scalar as object: string/long/double, or null for SQL NULL / no row.</summary>
    public object? QueryScalar(string sql)
    {
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public Dictionary<string, string> ReadMeta()
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM reperio_meta;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            meta[reader.GetString(0)] = reader.GetString(1);
        }

        return meta;
    }

    public bool TableExists(string name)
        => QueryScalarLong(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table', 'view') AND name = '{name}';") > 0;

    public void ExecuteNonQuery(string sql)
    {
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; temp cleanup must not fail tests.
        }
    }
}
