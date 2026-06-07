using Microsoft.Data.Sqlite;

namespace ReperioNet.Internal;

/// <summary>
/// Connection tuning scoped to one bulk write: a larger page cache (32 MiB) speeds the FTS b-tree
/// and merge work while a large index is built, and a raised WAL auto-checkpoint threshold stops
/// the load from being interrupted every ~4 MB of WAL growth. Both settings are captured on entry
/// and restored on dispose, so the steady-state configuration (PRD §8) is untouched outside the
/// bulk operation. Values are deliberately modest to stay mobile-friendly.
/// </summary>
internal sealed class BulkWriteTuning : IDisposable
{
    private const int BulkCacheSizeKib = -32768;        // negative = KiB => 32 MiB page cache
    private const int BulkWalAutocheckpointPages = 16384; // ~64 MiB at the default 4 KiB page size

    private readonly SqliteConnection _connection;
    private readonly long _previousCacheSize;
    private readonly long _previousWalAutocheckpoint;

    private BulkWriteTuning(SqliteConnection connection, long previousCacheSize, long previousWalAutocheckpoint)
    {
        _connection = connection;
        _previousCacheSize = previousCacheSize;
        _previousWalAutocheckpoint = previousWalAutocheckpoint;
    }

    /// <summary>Applies the bulk settings and captures the previous values. Call before opening the transaction.</summary>
    internal static BulkWriteTuning Apply(SqliteConnection connection)
    {
        var previousCacheSize = QueryLong(connection, "PRAGMA cache_size;");
        var previousWalAutocheckpoint = QueryLong(connection, "PRAGMA wal_autocheckpoint;");
        Execute(connection, $"PRAGMA cache_size = {BulkCacheSizeKib}; PRAGMA wal_autocheckpoint = {BulkWalAutocheckpointPages};");
        return new BulkWriteTuning(connection, previousCacheSize, previousWalAutocheckpoint);
    }

    /// <summary>Restores the previous settings (best effort if the connection is already broken).</summary>
    public void Dispose()
    {
        try
        {
            Execute(_connection, $"PRAGMA cache_size = {_previousCacheSize}; PRAGMA wal_autocheckpoint = {_previousWalAutocheckpoint};");
        }
        catch (SqliteException)
        {
            // The connection failed mid-batch; nothing to restore on a broken connection.
        }
        catch (InvalidOperationException)
        {
            // The connection was closed mid-batch.
        }
    }

    private static long QueryLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
