using System.Text.Json;
using Microsoft.Data.Sqlite;
using ReperioNet.Internal;

namespace ReperioNet;

/// <summary>
/// An embedded full-text search index over SQLite FTS5: typo-tolerant, substring-capable and
/// multilingual. The consumer supplies text plus a strongly-typed metadata payload; searches return
/// metadata plus a relevance score.
/// </summary>
/// <typeparam name="TMeta">The metadata type stored with each document. Serialization requires a
/// source-generated <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{TMeta}"/> via
/// <see cref="ReperioOptions{TMeta}.MetadataTypeInfo"/>.</typeparam>
/// <remarks>
/// <para><b>Storage:</b> the database file must live on <b>local storage</b>. The index uses SQLite
/// WAL journaling, which is unsafe on network file systems (SMB/NFS). The library takes the path as
/// given and does not police it.</para>
/// <para><b>Instancing:</b> use exactly one <see cref="SearchIndex{TMeta}"/> instance per database
/// file per process. Opening a second instance on the same file while another is live is
/// unsupported.</para>
/// <para><b>Concurrency:</b> all writes are serialized through a single-writer gate over one
/// dedicated connection; reads run on separate short-lived connections and may run concurrently with
/// each other and, under WAL, with the writer. Consumers never see <c>SQLITE_BUSY</c>.</para>
/// <para>All I/O methods are asynchronous and accept a <see cref="CancellationToken"/>; SQLite I/O is
/// synchronous under the hood and runs on a background thread so callers stay off the UI thread.</para>
/// </remarks>
public sealed class SearchIndex<TMeta> : IAsyncDisposable
{
    private static readonly IReadOnlyList<SearchHit<TMeta>> EmptyHits = Array.Empty<SearchHit<TMeta>>();

    private readonly SqliteConnection _writeConnection;
    private readonly string _connectionString;
    private readonly ReperioOptions<TMeta> _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    private SearchIndex(SqliteConnection writeConnection, string connectionString, ReperioOptions<TMeta> options)
    {
        _writeConnection = writeConnection;
        _connectionString = connectionString;
        _options = options;
    }

    /// <summary>
    /// Opens (creating if necessary) the search index at <paramref name="databasePath"/>.
    /// </summary>
    /// <param name="databasePath">Path of the SQLite database file. Must be on local storage (WAL is
    /// unsafe on SMB/NFS shares).</param>
    /// <param name="configure">Configures the <see cref="ReperioOptions{TMeta}"/>; at minimum,
    /// <see cref="ReperioOptions{TMeta}.MetadataTypeInfo"/> must be assigned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The opened index.</returns>
    /// <exception cref="ReperioException">The SQLite engine is older than 3.43.0; the FTS5 module is
    /// unavailable; the existing database has an incompatible schema or mismatched layout flags; or
    /// <see cref="ReperioOptions{TMeta}.MetadataTypeInfo"/> was not supplied.</exception>
    public static Task<SearchIndex<TMeta>> OpenAsync(
        string databasePath,
        Action<ReperioOptions<TMeta>>? configure = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(databasePath);

        var options = new ReperioOptions<TMeta>();
        configure?.Invoke(options);

        return Task.Run(
            () =>
            {
                // PRD §15.2 startup order (binding):
                // 1. Open write connection, apply PRAGMAs.
                var connectionString = SqliteConnectionFactory.BuildConnectionString(databasePath);
                var connection = SqliteConnectionFactory.Open(connectionString);
                try
                {
                    ct.ThrowIfCancellationRequested();

                    // 2. Verify sqlite_version() >= 3.43.0.
                    VerifySqliteVersion(connection);

                    // 3. Verify the FTS5 module is available.
                    VerifyFts5(connection);

                    // 4. Verify layout flags of an existing index, or create the schema and persist
                    //    schema_version + flags.
                    IndexSchema.EnsureSchema(connection, options);

                    // 5. Require a source-generated JsonTypeInfo for TMeta.
                    if (options.MetadataTypeInfo is null)
                    {
                        throw new ReperioException(
                            "ReperioOptions<TMeta>.MetadataTypeInfo is required. Supply a source-generated " +
                            "JsonTypeInfo<TMeta> (e.g. o.MetadataTypeInfo = MyJsonContext.Default.MyMeta) so " +
                            "metadata serialization is AOT- and trimming-safe; ReperioNet provides no " +
                            "reflection-based fallback.");
                    }

                    return new SearchIndex<TMeta>(connection, connectionString, options);
                }
                catch
                {
                    // Fully release the file on failure (pooled handles included).
                    connection.Close();
                    SqliteConnection.ClearPool(connection);
                    connection.Dispose();
                    throw;
                }
            },
            ct);
    }

    /// <summary>Adds <paramref name="entry"/> to the index, upserting by <see cref="SearchEntry{TMeta}.Id"/> (the internal rowid stays stable, so no duplicate hits).</summary>
    /// <param name="entry">The document to index.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException"><see cref="SearchEntry{TMeta}.Id"/> is empty.</exception>
    public Task AddAsync(SearchEntry<TMeta> entry, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateEntry(entry, nameof(entry));
        return RunWriteAsync(() => WriteEntries(new[] { entry }, nameof(entry), ct), ct);
    }

    /// <summary>Adds all <paramref name="entries"/> in one transaction, upserting by id.</summary>
    /// <param name="entries">The documents to index.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">An entry has an empty <see cref="SearchEntry{TMeta}.Id"/> (the whole batch is rolled back).</exception>
    public Task AddRangeAsync(IEnumerable<SearchEntry<TMeta>> entries, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entries);
        return RunWriteAsync(() => WriteEntries(entries, nameof(entries), ct), ct);
    }

    /// <summary>Removes the document with the given <paramref name="id"/>.</summary>
    /// <param name="id">The caller-provided document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="false"/> if no such document exists.</returns>
    public Task<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(id);
        return RunWriteAsync(() => RemoveCore(id), ct);
    }

    /// <summary>Returns whether a document with the given <paramref name="id"/> exists.</summary>
    /// <param name="id">The caller-provided document id.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<bool> ContainsAsync(string id, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(id);
        return RunReadAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM documents WHERE doc_id = @doc_id);";
                command.Parameters.AddWithValue("@doc_id", id);
                return (long)command.ExecuteScalar()! == 1;
            },
            ct);
    }

    /// <summary>Returns the number of indexed documents.</summary>
    /// <param name="ct">Cancellation token.</param>
    public Task<long> CountAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return RunReadAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM documents;";
                return (long)command.ExecuteScalar()!;
            },
            ct);
    }

    /// <summary>Removes all documents from the index.</summary>
    /// <param name="ct">Cancellation token.</param>
    public Task ClearAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return RunWriteAsync(ClearCore, ct);
    }

    /// <summary>Searches the index for <paramref name="query"/>.</summary>
    /// <param name="query">The user query; null/whitespace yields an empty result.</param>
    /// <param name="options">Per-query options; <see langword="null"/> uses defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Hits ordered by descending score (best bm25 match first).</returns>
    public Task<IReadOnlyList<SearchHit<TMeta>>> SearchAsync(
        string query,
        SearchQueryOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(EmptyHits);
        }

        var effectiveOptions = options ?? new SearchQueryOptions();
        return RunReadAsync(connection => SearchCore(connection, query, effectiveOptions), ct);
    }

    /// <summary>Runs FTS <c>optimize</c> on the search tables, then <c>PRAGMA optimize</c> and a WAL checkpoint.</summary>
    /// <param name="ct">Cancellation token.</param>
    public Task OptimizeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return RunWriteAsync(OptimizeCore, ct);
    }

    /// <summary>Drops and re-creates the FTS tables, then reindexes every document from the <c>documents</c> table.</summary>
    /// <param name="ct">Cancellation token.</param>
    public Task RebuildAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return RunWriteAsync(RebuildCore, ct);
    }

    /// <summary>Checkpoints the WAL (<c>wal_checkpoint(TRUNCATE)</c>), closes all connections and releases resources.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    using var command = _writeConnection.CreateCommand();
                    command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    command.ExecuteNonQuery();
                }
                finally
                {
                    // Close, then drain the pool so the file (and -wal/-shm) is fully released.
                    _writeConnection.Close();
                    SqliteConnection.ClearPool(_writeConnection);
                    _writeConnection.Dispose();
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        _writeLock.Dispose();
    }

    // ---- Write pipeline ------------------------------------------------------------------------

    /// <summary>Indexes entries inside one transaction on the dedicated write connection (PRD §6, §15.4, §15.5).</summary>
    private void WriteEntries(IEnumerable<SearchEntry<TMeta>> entries, string paramName, CancellationToken ct)
    {
        using var transaction = _writeConnection.BeginTransaction();
        using var batch = new UpsertBatch(_writeConnection, transaction, _options.EnableTrigram);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            ValidateEntry(entry, paramName);

            // §6.2: apply MaxContentChars before any processing.
            var text = ApplyMaxContentChars(entry.Content ?? string.Empty);

            // §6.3: resolve language (may stay null).
            var language = entry.Language ?? _options.LanguageDetector?.Detect(text) ?? _options.DefaultLanguage;

            // §15.4 column values (binding): raw text goes into base; stem/phonetic stay empty in M1–2;
            // rank_text holds the text only when content is not stored (no duplicate full-text storage).
            batch.Upsert(
                docId: entry.Id,
                language: language,
                metadataJson: JsonSerializer.Serialize(entry.Metadata, _options.MetadataTypeInfo),
                rankText: _options.StoreContent ? string.Empty : text,
                content: _options.StoreContent ? text : null,
                baseText: text,
                stem: string.Empty,
                phonetic: string.Empty);
        }

        transaction.Commit();
    }

    private bool RemoveCore(string id)
    {
        using var transaction = _writeConnection.BeginTransaction();

        long rowid;
        using (var find = _writeConnection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT rowid FROM documents WHERE doc_id = @doc_id;";
            find.Parameters.AddWithValue("@doc_id", id);
            if (find.ExecuteScalar() is not long found)
            {
                return false;
            }

            rowid = found;
        }

        using (var delete = _writeConnection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = _options.EnableTrigram
                ? """
                  DELETE FROM documents_fts WHERE rowid = @rowid;
                  DELETE FROM documents_trgm WHERE rowid = @rowid;
                  DELETE FROM documents WHERE rowid = @rowid;
                  """
                : """
                  DELETE FROM documents_fts WHERE rowid = @rowid;
                  DELETE FROM documents WHERE rowid = @rowid;
                  """;
            delete.Parameters.AddWithValue("@rowid", rowid);
            delete.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    private void ClearCore()
    {
        using var transaction = _writeConnection.BeginTransaction();
        using var command = _writeConnection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = _options.EnableTrigram
            ? """
              DELETE FROM documents;
              DELETE FROM documents_fts;
              DELETE FROM documents_trgm;
              """
            : """
              DELETE FROM documents;
              DELETE FROM documents_fts;
              """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void OptimizeCore()
    {
        using var command = _writeConnection.CreateCommand();
        command.CommandText = _options.EnableTrigram
            ? """
              INSERT INTO documents_fts(documents_fts) VALUES('optimize');
              INSERT INTO documents_trgm(documents_trgm) VALUES('optimize');
              PRAGMA optimize;
              PRAGMA wal_checkpoint(TRUNCATE);
              """
            : """
              INSERT INTO documents_fts(documents_fts) VALUES('optimize');
              PRAGMA optimize;
              PRAGMA wal_checkpoint(TRUNCATE);
              """;
        command.ExecuteNonQuery();
    }

    private void RebuildCore()
    {
        using var transaction = _writeConnection.BeginTransaction();
        IndexSchema.RecreateSearchTables(_writeConnection, transaction, _options.EnableTrigram);

        // Reindex from documents. The original (truncated) text is content when stored, else
        // rank_text (§15.4 invariant). stem/phonetic remain empty in Milestones 1–2.
        using var command = _writeConnection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = _options.EnableTrigram
            ? """
              INSERT INTO documents_fts (rowid, base, stem, phonetic)
              SELECT rowid, COALESCE(content, rank_text), '', '' FROM documents;
              INSERT INTO documents_trgm (rowid, text)
              SELECT rowid, COALESCE(content, rank_text) FROM documents;
              """
            : """
              INSERT INTO documents_fts (rowid, base, stem, phonetic)
              SELECT rowid, COALESCE(content, rank_text), '', '' FROM documents;
              """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    // ---- Read pipeline -------------------------------------------------------------------------

    /// <summary>Candidate-pool search: base MATCH + trigram recall, merged by rowid (PRD §9.5–9.9).</summary>
    private IReadOnlyList<SearchHit<TMeta>> SearchCore(SqliteConnection connection, string query, SearchQueryOptions options)
    {
        // §9.3: tokenize the query (base terms only until M5 adds stem/phonetic).
        var tokens = Tokenizer.Tokenize(query);

        // §9.6/§10: trigram recall only for queries of three or more characters.
        var useTrigram = _options.EnableTrigram && query.Length >= 3;
        if (tokens.Count == 0 && !useTrigram)
        {
            return EmptyHits;
        }

        // §9.7: gather candidates from both MATCH queries; merge by rowid keeping the best (lowest)
        // bm25 seen for that rowid.
        var bestRank = new Dictionary<long, double>();
        if (tokens.Count > 0)
        {
            // §9.5: base clause, plus the short-query prefix aid when the whole query is < 3 chars.
            var match = Fts5Match.BuildBaseMatch(tokens, prefixLastToken: query.Length < 3);
            CollectCandidates(connection, "documents_fts", match, options.CandidatePoolSize, bestRank);
        }

        if (useTrigram)
        {
            // §9.6: the escaped full query string against the trigram table (substring recall).
            CollectCandidates(connection, "documents_trgm", Fts5Match.EscapeToken(query), options.CandidatePoolSize, bestRank);
        }

        if (bestRank.Count == 0)
        {
            return EmptyHits;
        }

        // Keep the top CandidatePoolSize rowids ordered by bm25, lowest first (§9.7).
        var pool = bestRank
            .OrderBy(candidate => candidate.Value)
            .ThenBy(candidate => candidate.Key)
            .Take(options.CandidatePoolSize)
            .Select(candidate => (Rowid: candidate.Key, Rank: candidate.Value))
            .ToList();

        if (pool.Count == 0)
        {
            return EmptyHits;
        }

        // §9.8: load the candidates' documents. (rank_text/content join the load with fuzzy in M4.)
        var documents = LoadDocuments(connection, pool.Select(candidate => candidate.Rowid));

        // §9.9: normalize bm25 across the pool (lower is better; best maps to 1.0).
        var min = pool[0].Rank;
        var max = pool[0].Rank;
        foreach (var candidate in pool)
        {
            min = Math.Min(min, candidate.Rank);
            max = Math.Max(max, candidate.Rank);
        }

        // Order by score desc == bm25 asc (normalization is monotonic), doc_id asc as the stable
        // tiebreaker; then page. Metadata is deserialized only for the returned page.
        var page = pool
            .Where(candidate => documents.ContainsKey(candidate.Rowid))
            .Select(candidate => (candidate.Rank, Doc: documents[candidate.Rowid]))
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Doc.DocId, StringComparer.Ordinal)
            .Skip(Math.Max(0, options.Offset))
            .Take(Math.Max(0, options.Limit));

        var hits = new List<SearchHit<TMeta>>();
        foreach (var (rank, doc) in page)
        {
            var score = max > min ? (max - rank) / (max - min) : 1.0;
            var metadata = JsonSerializer.Deserialize(doc.MetadataJson, _options.MetadataTypeInfo)!;
            hits.Add(new SearchHit<TMeta>(doc.DocId, metadata, score));
        }

        return hits;
    }

    /// <summary>Runs one MATCH query and merges (rowid, bm25) results into <paramref name="bestRank"/>, keeping the lowest bm25 per rowid.</summary>
    private static void CollectCandidates(
        SqliteConnection connection,
        string table,
        string match,
        int poolSize,
        Dictionary<long, double> bestRank)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT rowid, bm25({table}) AS rank FROM {table} WHERE {table} MATCH @match ORDER BY rank, rowid LIMIT @pool;";
        command.Parameters.AddWithValue("@match", match);
        command.Parameters.AddWithValue("@pool", poolSize);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var rowid = reader.GetInt64(0);
            var rank = reader.GetDouble(1);
            if (!bestRank.TryGetValue(rowid, out var existing) || rank < existing)
            {
                bestRank[rowid] = rank;
            }
        }
    }

    /// <summary>Loads <c>doc_id</c> and metadata JSON for the candidate rowids (chunked IN queries).</summary>
    private static Dictionary<long, (string DocId, string MetadataJson)> LoadDocuments(
        SqliteConnection connection,
        IEnumerable<long> rowids)
    {
        var documents = new Dictionary<long, (string, string)>();
        foreach (var chunk in rowids.Chunk(500))
        {
            using var command = connection.CreateCommand();
            var names = new string[chunk.Length];
            for (var i = 0; i < chunk.Length; i++)
            {
                names[i] = "@r" + i;
                command.Parameters.AddWithValue(names[i], chunk[i]);
            }

            command.CommandText =
                $"SELECT rowid, doc_id, metadata FROM documents WHERE rowid IN ({string.Join(", ", names)});";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                documents[reader.GetInt64(0)] = (reader.GetString(1), reader.GetString(2));
            }
        }

        return documents;
    }

    // ---- Plumbing ------------------------------------------------------------------------------

    /// <summary>Serializes a write through the single-writer gate and runs it on a background thread.</summary>
    private async Task RunWriteAsync(Action work, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(work, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Serializes a write through the single-writer gate and runs it on a background thread.</summary>
    private async Task<T> RunWriteAsync<T>(Func<T> work, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(work, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Runs read work on a fresh short-lived connection (pooled) on a background thread.</summary>
    private Task<T> RunReadAsync<T>(Func<SqliteConnection, T> work, CancellationToken ct)
        => Task.Run(
            () =>
            {
                using var connection = SqliteConnectionFactory.Open(_connectionString);
                return work(connection);
            },
            ct);

    private string ApplyMaxContentChars(string content)
        => _options.MaxContentChars > 0 && content.Length > _options.MaxContentChars
            ? content[.._options.MaxContentChars]
            : content;

    private static void ValidateEntry(SearchEntry<TMeta>? entry, string paramName)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(paramName);
        }

        if (string.IsNullOrEmpty(entry.Id))
        {
            throw new ArgumentException("SearchEntry<TMeta>.Id must be a non-empty string.", paramName);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void VerifySqliteVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        var versionText = (string)command.ExecuteScalar()!;
        if (!SqliteVersionCheck.IsSupported(versionText))
        {
            throw new ReperioException(
                $"SQLite {versionText} is not supported: ReperioNet requires SQLite " +
                $"{SqliteVersionCheck.MinimumVersion} or newer (for contentless_delete=1 FTS5 tables).");
        }
    }

    private static void VerifyFts5(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE VIRTUAL TABLE temp.__fts5check USING fts5(x);
                DROP TABLE temp.__fts5check;
                """;
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ReperioException(
                "The SQLite FTS5 module is not available in this SQLite build. ReperioNet requires an " +
                "SQLite engine compiled with FTS5 (the bundled SQLitePCLRaw e_sqlite3 engine includes it).",
                ex);
        }
    }
}
