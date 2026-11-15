using Microsoft.Data.Sqlite;

namespace ReperioNet.Internal;

/// <summary>Builds connection strings and opens connections per PRD §15.1.</summary>
internal static class SqliteConnectionFactory
{
    /// <summary>The SQLite result code for "file is not a database", which SQLite3 Multiple Ciphers also returns for a wrong key.</summary>
    private const int SqliteNotADatabase = 26;

    /// <summary>
    /// Builds the canonical connection string: ReadWriteCreate, private cache, pooling on. With a key, the key goes in
    /// as a raw SQLite3 Multiple Ciphers key (<c>raw:</c> plus 64 hex digits), which Microsoft.Data.Sqlite applies with
    /// <c>PRAGMA key</c> on every physical connection it opens, pooled or not.
    /// </summary>
    internal static string BuildConnectionString(string databasePath, byte[]? encryptionKey = null)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
        };
        if (encryptionKey is not null) builder.Password = "raw:" + Convert.ToHexStringLower(encryptionKey);
        return builder.ToString();
    }

    /// <summary>Opens a connection and applies the per-connection PRAGMAs.</summary>
    internal static SqliteConnection Open(string connectionString) => Open(connectionString, encrypted: false);

    /// <summary>
    /// Opens a connection for one read beside the caller's own (2.7.3). It goes back to the pool without its page cache: a search
    /// that reads side by side opens up to a dozen and a half of them, and each kept up to 32 MB, about 600 MB for the mail of one
    /// account after a query that read many texts.
    /// </summary>
    internal static SideConnection OpenSide(string connectionString) => new(Open(connectionString, encrypted: false));

    /// <summary>A connection opened by <see cref="OpenSide"/>; disposing it frees its page cache and returns it to the pool.</summary>
    internal readonly struct SideConnection(SqliteConnection connection) : IDisposable
    {
        public SqliteConnection Connection => connection;

        public void Dispose()
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA shrink_memory;";
                command.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // A connection that failed keeps its cache until the pool drops it.
            }

            connection.Dispose();
        }
    }

    /// <summary>
    /// Opens the index's first connection. With <paramref name="encrypted"/>, the SQLite build must be SQLite3 Multiple
    /// Ciphers, checked before any PRAGMA that writes (a build without encryption would ignore the key and write the index
    /// in plaintext), and a file the key does not open is a <see cref="ReperioException"/>.
    /// </summary>
    internal static SqliteConnection Open(string connectionString, bool encrypted)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            try
            {
                try
                {
                    connection.Open();
                }
                catch (InvalidOperationException ex) when (encrypted)
                {
                    // Microsoft.Data.Sqlite refuses a password on a SQLite build it knows has no encryption.
                    throw NoEncryption(ex);
                }

                if (encrypted) VerifyEncryptingBuild(connection);
                ApplyPragmas(connection);
            }
            catch (SqliteException ex) when (encrypted && ex.SqliteErrorCode == SqliteNotADatabase)
            {
                // Microsoft.Data.Sqlite reads the file right after PRAGMA key, so a wrong key fails in Open already.
                throw new ReperioException(
                    "The index file could not be read with this encryption key: the key is wrong, or the file is not an index " +
                    "encrypted by ReperioNet.", ex);
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Fails unless the process runs on SQLite3 Multiple Ciphers (its <c>sqlite3mc_version()</c> SQL function exists).</summary>
    private static void VerifyEncryptingBuild(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT sqlite3mc_version();";
            command.ExecuteScalar();
        }
        catch (SqliteException ex)
        {
            throw NoEncryption(ex);
        }
    }

    private static ReperioException NoEncryption(Exception inner) => new(
        "ReperioOptions<TMeta>.EncryptionKey needs the SQLite3 Multiple Ciphers build (package SQLite3MC.PCLRaw.bundle " +
        "instead of SQLitePCLRaw.bundle_e_sqlite3). The SQLite build this process runs on cannot encrypt, so nothing was " +
        "written.", inner);

    /// <summary>
    /// Applies the per-connection PRAGMAs (PRD §8/§15.1). Must run on every opened connection,
    /// immediately after open, because pooled connections may come back with reset state.
    /// A 32 MiB page cache and 256 MiB of memory mapping keep the many small index probes of grouped
    /// queries (PRD §4a) off the page-cache-miss path.
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
            PRAGMA cache_size   = -32768;
            PRAGMA mmap_size    = 268435456;
            """;
        command.ExecuteNonQuery();
    }
}
