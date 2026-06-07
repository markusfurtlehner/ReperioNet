using System.Runtime.ExceptionServices;
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
    private const int AnalysisChunkSize = 256;

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

    /// <summary>One fully analyzed document, ready for the §15.5 upsert.</summary>
    private readonly record struct PreparedDocument(
        string DocId,
        string? Language,
        string MetadataJson,
        string RankText,
        string? Content,
        string BaseText,
        string Stem,
        string Phonetic);

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
    /// <remarks>
    /// The SQLite writes stay on the single dedicated write connection, but the text analysis
    /// (tokenization, stemming, phonetic encoding, metadata serialization) runs in parallel across
    /// CPU cores ahead of the writer, with stem/phonetic results memoized for the duration of the
    /// batch. Entries are written strictly in input order (for duplicate ids the last one wins).
    /// Because of this, <see cref="Abstractions.IStemmer"/>, <see cref="Abstractions.IPhoneticEncoder"/>,
    /// <see cref="Abstractions.IStopWordFilter"/> and <see cref="Abstractions.ILanguageDetector"/>
    /// implementations must be thread-safe (all bundled implementations are).
    /// </remarks>
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

    /// <summary>
    /// Indexes entries inside one transaction on the dedicated write connection (PRD §6, §15.4,
    /// §15.5). SQLite remains single-writer, but the pure-C# analysis (tokenize, stem, phonetic
    /// encode, metadata serialization) is parallelized chunk-by-chunk ahead of the writer; chunks
    /// are written strictly in input order, so duplicate-id "last wins" semantics are unchanged.
    /// Bulk batches additionally run with a temporarily enlarged page cache and WAL checkpoint
    /// threshold, restored when the batch ends.
    /// </summary>
    private void WriteEntries(IEnumerable<SearchEntry<TMeta>> entries, string paramName, CancellationToken ct)
    {
        var singleEntry = entries is SearchEntry<TMeta>[] { Length: 1 };
        using var tuning = singleEntry ? null : BulkWriteTuning.Apply(_writeConnection);
        using var transaction = _writeConnection.BeginTransaction();
        using var batch = new UpsertBatch(_writeConnection, transaction, _options.EnableTrigram);
        var cache = singleEntry ? null : new AnalysisCache(_options.Analyzers);

        foreach (var chunk in ReadChunks(entries, ct))
        {
            PreparedDocument[] prepared;
            if (chunk.Count == 1)
            {
                prepared = [PrepareDocument(chunk[0], paramName, cache)];
            }
            else
            {
                prepared = new PreparedDocument[chunk.Count];
                try
                {
                    Parallel.For(
                        0,
                        chunk.Count,
                        new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                        i => prepared[i] = PrepareDocument(chunk[i], paramName, cache));
                }
                catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
                {
                    // Surface the original exception type (e.g. ArgumentException for an invalid
                    // entry) exactly as the sequential pipeline did.
                    ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
                    throw;
                }
            }

            foreach (var document in prepared)
            {
                batch.Upsert(
                    document.DocId,
                    document.Language,
                    document.MetadataJson,
                    document.RankText,
                    document.Content,
                    document.BaseText,
                    document.Stem,
                    document.Phonetic);
            }
        }

        transaction.Commit();
    }

    /// <summary>The §6 analysis pipeline for one entry; safe to run concurrently across entries.</summary>
    private PreparedDocument PrepareDocument(SearchEntry<TMeta> entry, string paramName, AnalysisCache? cache)
    {
        ValidateEntry(entry, paramName);

        // §6.2: apply MaxContentChars before any processing.
        var text = ApplyMaxContentChars(entry.Content ?? string.Empty);

        // §6.3: resolve language (may stay null).
        var language = entry.Language ?? _options.LanguageDetector?.Detect(text) ?? _options.DefaultLanguage;

        // §6.4–6.5: derive the stem/phonetic streams with the language's analyzer (or fallback).
        var (stem, phonetic) = ComputeStreams(language, text, cache);

        // §15.4 column values (binding): raw text goes into base; rank_text holds the text only
        // when content is not stored (no duplicate full-text storage).
        return new PreparedDocument(
            entry.Id,
            language,
            JsonSerializer.Serialize(entry.Metadata, _options.MetadataTypeInfo),
            _options.StoreContent ? string.Empty : text,
            _options.StoreContent ? text : null,
            text,
            stem,
            phonetic);
    }

    /// <summary>Buffers the input into analysis chunks, honoring cancellation per entry as before.</summary>
    private static IEnumerable<List<SearchEntry<TMeta>>> ReadChunks(IEnumerable<SearchEntry<TMeta>> entries, CancellationToken ct)
    {
        var buffer = new List<SearchEntry<TMeta>>(AnalysisChunkSize);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            buffer.Add(entry);
            if (buffer.Count == AnalysisChunkSize)
            {
                yield return buffer;
                buffer = new List<SearchEntry<TMeta>>(AnalysisChunkSize);
            }
        }

        if (buffer.Count > 0)
        {
            yield return buffer;
        }
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

        // Reindex from documents: the original (truncated) text is content when stored, else
        // rank_text (§15.4 invariant); the stored language re-selects each document's analyzer so
        // the stem/phonetic streams are re-derived exactly as at add time.
        var rows = new List<(long Rowid, string? Language, string Text)>();
        using (var select = _writeConnection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT rowid, language, COALESCE(content, rank_text) FROM documents;";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
            }
        }

        using var insertFts = _writeConnection.CreateCommand();
        insertFts.Transaction = transaction;
        insertFts.CommandText =
            "INSERT INTO documents_fts (rowid, base, stem, phonetic) VALUES (@rowid, @base, @stem, @phonetic);";
        insertFts.Parameters.Add("@rowid", SqliteType.Integer);
        insertFts.Parameters.Add("@base", SqliteType.Text);
        insertFts.Parameters.Add("@stem", SqliteType.Text);
        insertFts.Parameters.Add("@phonetic", SqliteType.Text);
        insertFts.Prepare();

        using var insertTrigram = _options.EnableTrigram ? _writeConnection.CreateCommand() : null;
        if (insertTrigram is not null)
        {
            insertTrigram.Transaction = transaction;
            insertTrigram.CommandText = "INSERT INTO documents_trgm (rowid, text) VALUES (@rowid, @text);";
            insertTrigram.Parameters.Add("@rowid", SqliteType.Integer);
            insertTrigram.Parameters.Add("@text", SqliteType.Text);
            insertTrigram.Prepare();
        }

        var cache = new AnalysisCache(_options.Analyzers);
        foreach (var (rowid, language, text) in rows)
        {
            var (stem, phonetic) = ComputeStreams(language, text, cache);

            insertFts.Parameters["@rowid"].Value = rowid;
            insertFts.Parameters["@base"].Value = text;
            insertFts.Parameters["@stem"].Value = stem;
            insertFts.Parameters["@phonetic"].Value = phonetic;
            insertFts.ExecuteNonQuery();

            if (insertTrigram is not null)
            {
                insertTrigram.Parameters["@rowid"].Value = rowid;
                insertTrigram.Parameters["@text"].Value = text;
                insertTrigram.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    /// <summary>Derives the space-joined stem/phonetic streams for a document (§6.4–6.5).</summary>
    private (string Stem, string Phonetic) ComputeStreams(string? language, string text, AnalysisCache? cache = null)
    {
        if (!_options.EnableStemming && !_options.EnablePhonetic)
        {
            return (string.Empty, string.Empty);
        }

        var analyzer = cache is null ? Analysis.Resolve(_options.Analyzers, language) : cache.Resolve(language);
        var tokens = Tokenizer.Tokenize(text);
        var stem = _options.EnableStemming
            ? string.Join(' ', Analysis.StemTokens(analyzer, tokens, _options.RemoveStopWords))
            : string.Empty;
        var phonetic = _options.EnablePhonetic
            ? string.Join(' ', Analysis.PhoneticTokens(analyzer, tokens, _options.RemoveStopWords))
            : string.Empty;
        return (stem, phonetic);
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

        // §9.7: gather candidates; merge by rowid keeping the best (lowest) bm25 seen for that
        // rowid. With TermMatch.AllTerms (default) and a multi-token query, a strict pass requires
        // every base term first; an any-term pass widens recall only when the strict pass yields
        // fewer than Limit candidates. All-terms candidates always rank ahead (tier 0).
        var primary = new Dictionary<long, double>();
        var secondary = new Dictionary<long, double>();
        var useAllTerms = options.TermMatch == TermMatch.AllTerms && tokens.Count > 1;

        if (tokens.Count > 0)
        {
            // §9.2: resolve the query language and pick its analyzer (or the identity fallback).
            var language = options.Language ?? _options.LanguageDetector?.Detect(query) ?? _options.DefaultLanguage;
            var analyzer = Analysis.Resolve(_options.Analyzers, language);

            // §9.3: derive qStem/qPhon with the same processing as content.
            var stemTokens = _options.EnableStemming
                ? Analysis.StemTokens(analyzer, tokens, _options.RemoveStopWords)
                : null;
            var phoneticTokens = _options.EnablePhonetic && options.EnablePhonetic
                ? Analysis.PhoneticTokens(analyzer, tokens, _options.RemoveStopWords)
                : null;

            if (useAllTerms)
            {
                // Strict pass: implicit-AND on base only (cheap — the intersection is small).
                var andMatch = Fts5Match.BuildMatch(tokens, prefixLastToken: false, stemTokens: null, phoneticTokens: null, allTermsBase: true);
                CollectCandidates(connection, "documents_fts", andMatch, options.CandidatePoolSize, primary);

                if (primary.Count < Math.Max(0, options.Limit))
                {
                    // Recall fallback: the classic §9.5 any-term expression incl. stem/phonetic.
                    var orMatch = Fts5Match.BuildMatch(tokens, prefixLastToken: query.Length < 3, stemTokens, phoneticTokens);
                    CollectCandidates(connection, "documents_fts", orMatch, options.CandidatePoolSize, secondary);
                }
            }
            else
            {
                // §9.5: OR-combined column clauses, plus the short-query prefix aid on base when
                // the whole query is < 3 chars.
                var match = Fts5Match.BuildMatch(tokens, prefixLastToken: query.Length < 3, stemTokens, phoneticTokens);
                CollectCandidates(connection, "documents_fts", match, options.CandidatePoolSize, primary);
            }
        }

        if (useTrigram)
        {
            // §9.6: the escaped full query string against the trigram table (substring recall).
            // In all-terms mode substring matches join the fallback tier unless the document also
            // satisfied the strict pass.
            CollectCandidates(connection, "documents_trgm", Fts5Match.EscapeToken(query), options.CandidatePoolSize, useAllTerms ? secondary : primary);
        }

        // Dedup across tiers: a rowid seen by the strict pass stays tier 0 with its best bm25.
        if (secondary.Count > 0)
        {
            foreach (var (rowid, rank) in secondary)
            {
                if (primary.TryGetValue(rowid, out var existing))
                {
                    primary[rowid] = Math.Min(existing, rank);
                }
            }

            foreach (var rowid in primary.Keys)
            {
                secondary.Remove(rowid);
            }
        }

        if (primary.Count == 0 && secondary.Count == 0)
        {
            return EmptyHits;
        }

        // Keep the top CandidatePoolSize rowids ordered by tier, then bm25 (lowest first; §9.7).
        var pool = primary
            .Select(candidate => (Rowid: candidate.Key, Rank: candidate.Value, Tier: 0))
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Rowid)
            .Concat(secondary
                .Select(candidate => (Rowid: candidate.Key, Rank: candidate.Value, Tier: 1))
                .OrderBy(candidate => candidate.Rank)
                .ThenBy(candidate => candidate.Rowid))
            .Take(options.CandidatePoolSize)
            .ToList();

        if (pool.Count == 0)
        {
            return EmptyHits;
        }

        // §9.8: load metadata, rank_text and content (if stored) for each candidate.
        var documents = LoadDocuments(connection, pool.Select(candidate => candidate.Rowid));

        // §9.9: normalize bm25 across the pool (lower is better; best maps to 1.0).
        var min = pool[0].Rank;
        var max = pool[0].Rank;
        foreach (var candidate in pool)
        {
            min = Math.Min(min, candidate.Rank);
            max = Math.Max(max, candidate.Rank);
        }

        var foldedQuery = TextFold.Fold(query);
        var scored = new List<(int Tier, double Score, string DocId, string MetadataJson, string? Content)>(pool.Count);
        foreach (var (rowid, rank, tier) in pool)
        {
            if (!documents.TryGetValue(rowid, out var doc))
            {
                continue;
            }

            var normBm25 = max > min ? (max - rank) / (max - min) : 1.0;

            // §9.10: fuzzy pass against content when stored, else rank_text.
            var text = doc.Content ?? doc.RankText;

            // §9.11: blend, or pure normalized bm25 when fuzzy is off.
            var score = options.EnableFuzzy
                ? (0.6 * _options.FuzzyRanker.Score(query, text)) + (0.4 * normBm25)
                : normBm25;

            // §9.11 exact-match boost: folded text contains the folded raw query as a substring.
            if (foldedQuery.Length > 0 && TextFold.Fold(text).Contains(foldedQuery, StringComparison.Ordinal))
            {
                score = Math.Min(1.0, score + 0.15);
            }

            // §9.12: drop scores below MinScore.
            if (score < options.MinScore)
            {
                continue;
            }

            scored.Add((tier, score, doc.DocId, doc.MetadataJson, doc.Content));
        }

        // §9.12: order by tier (all-terms matches ahead of fallback matches), then score desc,
        // doc_id asc as the stable tiebreaker; then page.
        var page = scored
            .OrderBy(candidate => candidate.Tier)
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.DocId, StringComparer.Ordinal)
            .Skip(Math.Max(0, options.Offset))
            .Take(Math.Max(0, options.Limit));

        // §9.13: project to SearchHit; metadata is deserialized (and snippets built) only for the page.
        var includeSnippet = options.IncludeSnippet && _options.StoreContent;
        var hits = new List<SearchHit<TMeta>>();
        foreach (var (_, score, docId, metadataJson, content) in page)
        {
            var metadata = JsonSerializer.Deserialize(metadataJson, _options.MetadataTypeInfo)!;
            var snippet = includeSnippet && content is not null
                ? SnippetBuilder.Build(content, tokens, options.Snippet)
                : null;
            hits.Add(new SearchHit<TMeta>(docId, metadata, score, snippet));
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

    /// <summary>Loads <c>doc_id</c>, metadata JSON, <c>rank_text</c> and <c>content</c> for the candidate rowids (chunked IN queries, §9.8).</summary>
    private static Dictionary<long, (string DocId, string MetadataJson, string RankText, string? Content)> LoadDocuments(
        SqliteConnection connection,
        IEnumerable<long> rowids)
    {
        var documents = new Dictionary<long, (string, string, string, string?)>();
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
                $"SELECT rowid, doc_id, metadata, rank_text, content FROM documents WHERE rowid IN ({string.Join(", ", names)});";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                documents[reader.GetInt64(0)] = (
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4));
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
