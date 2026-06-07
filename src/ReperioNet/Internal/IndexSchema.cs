using Microsoft.Data.Sqlite;

namespace ReperioNet.Internal;

/// <summary>Schema creation and <c>reperio_meta</c> versioning/layout-flag handling (PRD §5, §15.2).</summary>
internal static class IndexSchema
{
    /// <summary>Current schema version written to and required in <c>reperio_meta</c>.</summary>
    internal const string SchemaVersion = "1";

    /// <summary>The fixed tokenizer recorded in <c>reperio_meta</c>.</summary>
    internal const string Tokenizer = "unicode61 remove_diacritics 2";

    internal const string SchemaVersionKey = "schema_version";
    internal const string StoreContentKey = "store_content";
    internal const string EnableTrigramKey = "enable_trigram";
    internal const string EnableStemmingKey = "enable_stemming";
    internal const string EnablePhoneticKey = "enable_phonetic";
    internal const string RemoveStopWordsKey = "remove_stop_words";
    internal const string TokenizerKey = "tokenizer";

    private const string CreateMetaTableSql =
        """
        CREATE TABLE IF NOT EXISTS reperio_meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;

    private const string CreateDocumentsTableSql =
        """
        CREATE TABLE IF NOT EXISTS documents (
            rowid     INTEGER PRIMARY KEY,   -- internal; reused on update so FTS rows stay aligned
            doc_id    TEXT NOT NULL UNIQUE,  -- caller-provided stable id
            language  TEXT,                  -- resolved ISO code or NULL
            metadata  TEXT NOT NULL,         -- JSON (TMeta)
            rank_text TEXT NOT NULL,         -- normalized base token stream (used for fuzzy re-rank)
            content   TEXT                   -- original content; NULL unless StoreContent = true
        );
        """;

    private const string CreateFtsTableSql =
        """
        CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(
            base, stem, phonetic,
            content='',
            contentless_delete=1,
            tokenize='unicode61 remove_diacritics 2'
        );
        """;

    private const string CreateTrigramTableSql =
        """
        CREATE VIRTUAL TABLE IF NOT EXISTS documents_trgm USING fts5(
            text,
            content='',
            contentless_delete=1,
            tokenize='trigram'
        );
        """;

    private const string DropSearchTablesSql =
        """
        DROP TABLE IF EXISTS documents_fts;
        DROP TABLE IF EXISTS documents_trgm;
        """;

    /// <summary>
    /// PRD §15.2 step 4: if <c>reperio_meta</c> exists, verify the schema version and layout flags
    /// against the requested options (throw <see cref="ReperioException"/> on mismatch); otherwise
    /// create the schema (respecting <c>EnableTrigram</c>) and persist version + flags.
    /// </summary>
    internal static void EnsureSchema<TMeta>(SqliteConnection connection, ReperioOptions<TMeta> options)
    {
        if (TableExists(connection, "reperio_meta"))
        {
            var stored = ReadMeta(connection);
            VerifySchemaVersion(stored);
            VerifyLayoutFlags(stored, options);
        }
        else
        {
            using var transaction = connection.BeginTransaction();
            CreateTables(connection, transaction, options.EnableTrigram);
            WriteMeta(connection, transaction, options);
            transaction.Commit();
        }
    }

    /// <summary>
    /// Drops and re-creates the FTS tables (respecting <paramref name="enableTrigram"/>) for
    /// <c>RebuildAsync</c>. Must run inside <paramref name="transaction"/>.
    /// </summary>
    internal static void RecreateSearchTables(SqliteConnection connection, SqliteTransaction transaction, bool enableTrigram)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = enableTrigram
            ? DropSearchTablesSql + CreateFtsTableSql + CreateTrigramTableSql
            : DropSearchTablesSql + CreateFtsTableSql;
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
        command.Parameters.AddWithValue("@name", tableName);
        return (long)command.ExecuteScalar()! > 0;
    }

    private static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM reperio_meta;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            meta[reader.GetString(0)] = reader.GetString(1);
        }

        return meta;
    }

    private static void VerifySchemaVersion(Dictionary<string, string> stored)
    {
        stored.TryGetValue(SchemaVersionKey, out var version);
        if (!string.Equals(version, SchemaVersion, StringComparison.Ordinal))
        {
            throw new ReperioException(
                $"The database has an incompatible schema: '{SchemaVersionKey}' is '{version ?? "<missing>"}', " +
                $"but this version of ReperioNet requires schema_version '{SchemaVersion}'.");
        }
    }

    private static void VerifyLayoutFlags<TMeta>(Dictionary<string, string> stored, ReperioOptions<TMeta> options)
    {
        foreach (var (key, requested) in EnumerateLayoutFlags(options))
        {
            stored.TryGetValue(key, out var persisted);
            if (!string.Equals(persisted, requested, StringComparison.Ordinal))
            {
                throw new ReperioException(
                    $"Index layout mismatch for flag '{key}': the database was created with " +
                    $"'{persisted ?? "<missing>"}' but the requested options require '{requested}'. " +
                    "Open the index with matching options, or call RebuildAsync() to re-create the index " +
                    "with the new layout.");
            }
        }
    }

    private static void CreateTables(SqliteConnection connection, SqliteTransaction transaction, bool enableTrigram)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = enableTrigram
            ? CreateMetaTableSql + CreateDocumentsTableSql + CreateFtsTableSql + CreateTrigramTableSql
            : CreateMetaTableSql + CreateDocumentsTableSql + CreateFtsTableSql;
        command.ExecuteNonQuery();
    }

    private static void WriteMeta<TMeta>(SqliteConnection connection, SqliteTransaction transaction, ReperioOptions<TMeta> options)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO reperio_meta (key, value) VALUES (@key, @value);";
        var keyParameter = command.Parameters.Add("@key", SqliteType.Text);
        var valueParameter = command.Parameters.Add("@value", SqliteType.Text);

        keyParameter.Value = SchemaVersionKey;
        valueParameter.Value = SchemaVersion;
        command.ExecuteNonQuery();

        foreach (var (key, value) in EnumerateLayoutFlags(options))
        {
            keyParameter.Value = key;
            valueParameter.Value = value;
            command.ExecuteNonQuery();
        }
    }

    private static IEnumerable<(string Key, string Value)> EnumerateLayoutFlags<TMeta>(ReperioOptions<TMeta> options)
    {
        yield return (StoreContentKey, FormatBool(options.StoreContent));
        yield return (EnableTrigramKey, FormatBool(options.EnableTrigram));
        yield return (EnableStemmingKey, FormatBool(options.EnableStemming));
        yield return (EnablePhoneticKey, FormatBool(options.EnablePhonetic));
        yield return (RemoveStopWordsKey, FormatBool(options.RemoveStopWords));
        yield return (TokenizerKey, Tokenizer);
    }

    private static string FormatBool(bool value) => value ? "true" : "false";
}
