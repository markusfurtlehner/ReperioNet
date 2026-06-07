using Microsoft.Data.Sqlite;

namespace ReperioNet.Internal;

/// <summary>Builds connection strings and opens connections per PRD §15.1.</summary>
internal static class SqliteConnectionFactory
{
    /// <summary>Builds the canonical connection string: ReadWriteCreate, private cache, pooling on.</summary>
    internal static string BuildConnectionString(string databasePath)
        => new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
        }.ToString();

    /// <summary>Opens a connection and applies the per-connection PRAGMAs.</summary>
    internal static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            ApplyPragmas(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Applies the per-connection PRAGMAs (PRD §8/§15.1). Must run on every opened connection,
    /// immediately after open, because pooled connections may come back with reset state.
    /// </summary>
    internal static void ApplyPragmas(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous  = NORMAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            PRAGMA temp_store   = MEMORY;
            """;
        command.ExecuteNonQuery();
    }
}
