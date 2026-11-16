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
public sealed partial class SearchIndex<TMeta> : IAsyncDisposable
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
        string Phonetic,
        EntryFields? Fields);

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
        if (options.EncryptionKey is { Length: not 32 })
        {
            throw new ArgumentException(
                "ReperioOptions<TMeta>.EncryptionKey must be exactly 32 bytes.", nameof(configure));
        }

        return Task.Run(
            () =>
            {
                // PRD §15.2 startup order (binding):
                // 1. Open write connection, apply PRAGMAs.
                var connectionString = SqliteConnectionFactory.BuildConnectionString(databasePath, options.EncryptionKey);
                var connection = SqliteConnectionFactory.Open(connectionString, encrypted: options.EncryptionKey is not null);
                try
                {
                    ct.ThrowIfCancellationRequested();

                    // 2. Verify sqlite_version() >= 3.43.0.
                    VerifySqliteVersion(connection);

                    // 3. Verify the FTS5 module is available.
                    VerifyFts5(connection);

                    options.Fields.Validate();

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
        return RunReadAsync(connection => Project(SearchCore(connection, query, effectiveOptions, keysOnly: false)), ct);
    }

    /// <summary>
    /// Searches the index like <see cref="SearchAsync"/> and returns the matches without their metadata (2.4): id, score and
    /// snippet. With <see cref="SearchQueryOptions.ScorePoolSize"/> set, the candidates that are not scored are read from a slim
    /// rowid-to-id index only, so a query with tens of thousands of matches does not touch their stored rows.
    /// </summary>
    /// <param name="query">The user query; null/whitespace yields an empty result.</param>
    /// <param name="options">Per-query options; <see langword="null"/> uses defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Matches in the order <see cref="SearchAsync"/> returns its hits.</returns>
    public Task<IReadOnlyList<SearchMatch>> SearchMatchesAsync(
        string query,
        SearchQueryOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<SearchMatch>>(Array.Empty<SearchMatch>());
        }

        var effectiveOptions = options ?? new SearchQueryOptions();
        return RunReadAsync<IReadOnlyList<SearchMatch>>(connection =>
        {
            var raw = SearchCore(connection, query, effectiveOptions, keysOnly: true);
            var matches = new SearchMatch[raw.Count];
            for (var i = 0; i < raw.Count; i++)
            {
                matches[i] = new SearchMatch(raw[i].DocId, raw[i].Score, raw[i].Snippet);
            }

            return matches;
        }, ct);
    }

    /// <summary>
    /// Scores the given documents against a query and returns them with score and snippet, best first (2.6): the fuzzy score and
    /// the exact-match boost of <see cref="SearchAsync"/>, with the bm25 share at its best value, because no full-text query runs.
    /// It is the second step after a search that came back unranked (<see cref="SearchQueryOptions.RankLimit"/>): the caller picks
    /// the matches that matter to it (the newest, say) and has only those scored. An id the index does not hold is left out, and
    /// so is a document under <see cref="SearchQueryOptions.MinScore"/>. Whether a document matches the query is not checked.
    /// </summary>
    /// <param name="query">The user query; null/whitespace yields an empty result.</param>
    /// <param name="ids">The ids of the documents to score.</param>
    /// <param name="options">Per-query options; <see langword="null"/> uses defaults. Offset, limit and the pool sizes do not apply.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<IReadOnlyList<SearchMatch>> ScoreMatchesAsync(
        string query,
        IReadOnlyCollection<string> ids,
        SearchQueryOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ids);
        if (string.IsNullOrWhiteSpace(query) || ids.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<SearchMatch>>(Array.Empty<SearchMatch>());
        }

        var effectiveOptions = options ?? new SearchQueryOptions();
        return RunReadAsync<IReadOnlyList<SearchMatch>>(connection => ScoreCore(connection, query, ids, effectiveOptions), ct);
    }

    /// <summary>
    /// The texts the given documents were added with, by id (2.7): what <c>AddAsync</c> was given, whether or not the index stores
    /// content. An id the index does not hold is left out. A caller that has to test a text itself (a phrase character by
    /// character, say) reads it here and does not have to keep a copy of every text. A long list is read in parts, side by side.
    /// </summary>
    /// <param name="ids">The ids of the documents.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<IReadOnlyDictionary<string, string>> GetTextsAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        return RunReadAsync<IReadOnlyDictionary<string, string>>(connection =>
        {
            var documents = LoadTextsInParts(connection, ids.Distinct(StringComparer.Ordinal).ToArray());
            var texts = new Dictionary<string, string>(documents.Count, StringComparer.Ordinal);
            foreach (var (docId, rankText, content) in documents)
            {
                texts[docId] = content ?? rankText;
            }

            return texts;
        }, ct);
    }

    /// <summary>
    /// The ids of the documents whose text contains <paramref name="value"/> anywhere, ignoring case (2.7): a plain substring test
    /// (no tokens, no word forms, no typo tolerance), answered from the trigram index without reading one text.
    /// <see langword="null"/> when the index cannot answer it: there is no trigram index
    /// (<see cref="ReperioOptions{TMeta}.EnableTrigram"/>) or the value is shorter than three characters. The caller then tests the
    /// texts itself (<see cref="GetTextsAsync"/>). Case is folded the way the trigram tokenizer of SQLite folds it.
    /// </summary>
    /// <param name="value">The text to look for, as it is (leading and trailing spaces count).</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<IReadOnlySet<string>?> TryFindContainingAsync(string value, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(value);
        if (!_options.EnableTrigram || !HasThreeCharacters(value))
        {
            return Task.FromResult<IReadOnlySet<string>?>(null);
        }

        return RunReadAsync<IReadOnlySet<string>?>(connection =>
        {
            var rowids = new List<long>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT rowid FROM documents_trgm WHERE documents_trgm MATCH @match;";
                command.Parameters.AddWithValue("@match", Fts5Match.EscapeToken(value));
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rowids.Add(reader.GetInt64(0));
                }
            }

            var found = new HashSet<string>(rowids.Count, StringComparer.Ordinal);
            foreach (var (docId, _) in LoadDocumentIdsInParts(connection, rowids).Values)
            {
                found.Add(docId);
            }

            return found;
        }, ct);
    }

    /// <summary>
    /// Of the given documents, the ids of those whose text contains <paramref name="value"/>, ignoring case (ordinal), each within
    /// the part of its text the slice names (2.7.1). The texts are read and tested here, in parts side by side, in buffers that are
    /// used again: the caller gets the ids and no text is handed out. It answers for any value and any index layout, at the
    /// cost of reading the texts of the documents asked for; <see cref="TryFindContainingAsync"/> narrows them first where it can.
    /// An id the index does not hold is left out.
    /// </summary>
    /// <param name="value">The text to look for, as it is.</param>
    /// <param name="documents">The documents to test, each with the part of its text that counts.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<IReadOnlySet<string>> FindContainingAmongAsync(string value, IReadOnlyCollection<TextSlice> documents, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
        {
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
        }

        return RunReadAsync<IReadOnlySet<string>>(connection =>
        {
            var slices = documents.ToArray();
            var parts = Math.Min(Math.Min(Environment.ProcessorCount, 8), slices.Length / TextPartSize);
            if (parts < 2)
            {
                return new HashSet<string>(ContainingAmong(connection, slices, value, ct), StringComparer.Ordinal);
            }

            var size = (slices.Length + parts - 1) / parts;
            var others = new Task<List<string>>[parts - 1];
            for (var i = 1; i < parts; i++)
            {
                var part = new ArraySegment<TextSlice>(slices, i * size, Math.Min(size, slices.Length - (i * size)));
                others[i - 1] = Task.Run(() =>
                {
                    using var ownSide = SqliteConnectionFactory.OpenSide(_connectionString);
                    var own = ownSide.Connection;
                    return ContainingAmong(own, part, value, ct);
                });
            }

            HashSet<string> found;
            try
            {
                found = new HashSet<string>(ContainingAmong(connection, new ArraySegment<TextSlice>(slices, 0, size), value, ct), StringComparer.Ordinal);
            }
            catch
            {
                // The other reads must not outlive a failed call unobserved.
                try { Task.WaitAll(others); }
                catch { }

                throw;
            }

            foreach (var other in others)
            {
                found.UnionWith(other.GetAwaiter().GetResult());
            }

            return found;
        }, ct);
    }

    /// <summary>The ids of the given documents whose text slice contains the value, read on one connection without a string per text.</summary>
    private static List<string> ContainingAmong(SqliteConnection connection, IReadOnlyList<TextSlice> slices, string value, CancellationToken ct)
    {
        var found = new List<string>();
        var chars = System.Buffers.ArrayPool<char>.Shared.Rent(16 * 1024);
        try
        {
            foreach (var chunk in slices.Chunk(500))
            {
                ct.ThrowIfCancellationRequested();
                var byId = new Dictionary<string, TextSlice>(chunk.Length, StringComparer.Ordinal);
                using var command = connection.CreateCommand();
                var names = new string[chunk.Length];
                for (var i = 0; i < chunk.Length; i++)
                {
                    names[i] = "@d" + i;
                    command.Parameters.AddWithValue(names[i], chunk[i].Id);
                    byId[chunk[i].Id] = chunk[i];
                }

                command.CommandText =
                    $"SELECT doc_id, COALESCE(content, rank_text) FROM documents WHERE doc_id IN ({string.Join(", ", names)});";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    // The text is taken as the UTF-8 bytes SQLite holds for the row: asking the reader for the value would make
                    // an array or a string of every text.
                    var id = reader.GetString(0);
                    var utf8 = SQLitePCL.raw.sqlite3_column_blob(reader.Handle, 1);
                    var length = utf8.Length;
                    if (chars.Length < length)
                    {
                        System.Buffers.ArrayPool<char>.Shared.Return(chars);
                        chars = System.Buffers.ArrayPool<char>.Shared.Rent(length);   // UTF-8 never has fewer bytes than UTF-16 has units
                    }

                    var text = chars.AsSpan(0, System.Text.Encoding.UTF8.GetChars(utf8, chars));
                    var slice = byId[id];
                    var start = Math.Clamp(slice.Start, 0, text.Length);
                    var count = slice.Length < 0 ? text.Length - start : Math.Min(slice.Length, text.Length - start);
                    if (text.Slice(start, count).Contains(value.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(id);
                    }
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(chars);
        }

        return found;
    }

    private static bool HasThreeCharacters(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            if (++count == 3)
            {
                return true;
            }
        }

        return false;
    }

    private SearchMatch[] ScoreCore(SqliteConnection connection, string query, IReadOnlyCollection<string> ids, SearchQueryOptions options)
    {
        var documents = LoadTextsInParts(connection, ids.Distinct(StringComparer.Ordinal).ToArray());

        var tokens = Tokenizer.Tokenize(query);
        var foldedQuery = TextFold.Fold(query);
        var includeSnippet = options.IncludeSnippet && _options.StoreContent;
        var scores = new double[documents.Count];
        var snippets = new string?[documents.Count];
        void Score(int i)
        {
            var text = documents[i].Content ?? documents[i].RankText;
            var score = options.EnableFuzzy ? (0.6 * _options.FuzzyRanker.Score(query, text)) + 0.4 : 1.0;

            // The text is folded once, for the boost and for the snippet (2.6.2).
            var content = includeSnippet ? documents[i].Content : null;
            using var map = content is null ? null : TextFold.FoldPooled(content);
            if (foldedQuery.Length > 0
                && (map is null
                    ? TextFold.Fold(text).Contains(foldedQuery, StringComparison.Ordinal)
                    : map.Span.IndexOf(foldedQuery.AsSpan(), StringComparison.Ordinal) >= 0))
            {
                score = Math.Min(1.0, score + 0.15);
            }

            scores[i] = score;
            if (content is not null && score >= options.MinScore)
            {
                snippets[i] = SnippetBuilder.Build(content, map!, tokens, options.Snippet);
            }
        }

        if (documents.Count >= ParallelScoreFrom)
        {
            Parallel.For(0, documents.Count, Score);
        }
        else
        {
            for (var i = 0; i < documents.Count; i++)
            {
                Score(i);
            }
        }

        return Enumerable.Range(0, documents.Count)
            .Where(i => scores[i] >= options.MinScore)
            .OrderByDescending(i => scores[i])
            .ThenBy(i => documents[i].DocId, StringComparer.Ordinal)
            .Select(i => new SearchMatch(documents[i].DocId, scores[i], snippets[i]))
            .ToArray();
    }

    /// <summary>A list of ids is read with its texts in parts of this many (2.6.3).</summary>
    private const int TextPartSize = 64;

    /// <summary>
    /// The stored texts of the given documents (2.6.3). From two parts on, the parts are read side by side, each on a connection of
    /// its own: reading the 500 mail texts of a scored list (4 MB) in one statement took longer than scoring them on every core.
    /// </summary>
    private List<(string DocId, string RankText, string? Content)> LoadTextsInParts(SqliteConnection connection, string[] ids)
    {
        var parts = Math.Min(Math.Min(Environment.ProcessorCount, 8), ids.Length / TextPartSize);
        if (parts < 2)
        {
            return LoadTexts(connection, ids);
        }

        var size = (ids.Length + parts - 1) / parts;
        var others = new Task<List<(string DocId, string RankText, string? Content)>>[parts - 1];
        for (var i = 1; i < parts; i++)
        {
            var part = new ArraySegment<string>(ids, i * size, Math.Min(size, ids.Length - (i * size)));
            others[i - 1] = Task.Run(() =>
            {
                using var ownSide = SqliteConnectionFactory.OpenSide(_connectionString);
                var own = ownSide.Connection;
                return LoadTexts(own, part);
            });
        }

        List<(string DocId, string RankText, string? Content)> all;
        try
        {
            all = LoadTexts(connection, new ArraySegment<string>(ids, 0, size));
        }
        catch
        {
            // The other reads must not outlive a failed call unobserved.
            try { Task.WaitAll(others); }
            catch { }

            throw;
        }

        all.EnsureCapacity(ids.Length);
        foreach (var other in others)
        {
            all.AddRange(other.GetAwaiter().GetResult());
        }

        return all;
    }

    /// <summary>The stored texts of the given documents, read on one connection.</summary>
    private static List<(string DocId, string RankText, string? Content)> LoadTexts(SqliteConnection connection, IReadOnlyList<string> ids)
    {
        var documents = new List<(string DocId, string RankText, string? Content)>(ids.Count);
        foreach (var chunk in ids.Chunk(500))
        {
            using var command = connection.CreateCommand();
            var names = new string[chunk.Length];
            for (var i = 0; i < chunk.Length; i++)
            {
                names[i] = "@d" + i;
                command.Parameters.AddWithValue(names[i], chunk[i]);
            }

            command.CommandText = $"SELECT doc_id, rank_text, content FROM documents WHERE doc_id IN ({string.Join(", ", names)});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                documents.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        return documents;
    }

    /// <summary>§9.13: metadata is deserialized only for the returned page.</summary>
    private IReadOnlyList<SearchHit<TMeta>> Project(List<RawHit> raw)
    {
        if (raw.Count == 0)
        {
            return EmptyHits;
        }

        var hits = new List<SearchHit<TMeta>>(raw.Count);
        foreach (var hit in raw)
        {
            hits.Add(new SearchHit<TMeta>(hit.DocId, JsonSerializer.Deserialize(hit.MetadataJson!, _options.MetadataTypeInfo)!, hit.Score, hit.Snippet));
        }

        return hits;
    }

    private readonly record struct RawHit(string DocId, string? MetadataJson, double Score, string? Snippet);

    private static readonly List<RawHit> NoRawHits = new();

    /// <summary>From this many candidates the scoring against their texts runs in parallel.</summary>
    private const int ParallelScoreFrom = 64;

    /// <summary>From this many unscored candidates their ids are read on a second connection beside the scoring.</summary>
    private const int ParallelTailFrom = 1024;

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
        using var batch = new UpsertBatch(_writeConnection, transaction, _options.EnableTrigram, _options.Fields);
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
                    document.Phonetic,
                    document.Fields);
            }
        }

        transaction.Commit();
    }

    /// <summary>The §6 analysis pipeline for one entry; safe to run concurrently across entries.</summary>
    private PreparedDocument PrepareDocument(SearchEntry<TMeta> entry, string paramName, AnalysisCache? cache)
    {
        ValidateEntry(entry, paramName);
        UpsertBatch.Validate(_options.Fields, entry.Fields);

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
            phonetic,
            entry.Fields);
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
                  DELETE FROM doc_keys WHERE rowid = @rowid;
                  DELETE FROM doc_nums WHERE rowid = @rowid;
                  DELETE FROM documents WHERE rowid = @rowid;
                  """
                : """
                  DELETE FROM documents_fts WHERE rowid = @rowid;
                  DELETE FROM doc_keys WHERE rowid = @rowid;
                  DELETE FROM doc_nums WHERE rowid = @rowid;
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
              DELETE FROM doc_keys;
              DELETE FROM doc_nums;
              DELETE FROM documents_fts;
              DELETE FROM documents_trgm;
              """
            : """
              DELETE FROM documents;
              DELETE FROM doc_keys;
              DELETE FROM doc_nums;
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
    private List<RawHit> SearchCore(SqliteConnection connection, string query, SearchQueryOptions options, bool keysOnly)
    {
        // §9.3: tokenize the query (base terms only until M5 adds stem/phonetic).
        var tokens = Tokenizer.Tokenize(query);

        // §9.6/§10: trigram recall only for queries of three or more characters.
        var useTrigram = _options.EnableTrigram && query.Length >= 3;
        if (tokens.Count == 0 && !useTrigram)
        {
            return NoRawHits;
        }

        // §9.7: gather candidates; merge by rowid keeping the best (lowest) bm25 seen for that
        // rowid. With TermMatch.AllTerms (default) and a multi-token query, a strict pass requires
        // every base term first; an any-term pass widens recall only when the strict pass yields
        // fewer than Limit candidates. All-terms candidates always rank ahead (tier 0).
        var useAllTerms = options.TermMatch == TermMatch.AllTerms && tokens.Count > 1;

        // 2.6: with a RankLimit the matches are first only listed (no bm25, which visits every match). A query with more matches
        // than the limit is answered from that list, unranked; any other is gathered again with its ranks, which is cheap then.
        Dictionary<long, double> primary;
        Dictionary<long, double> secondary;
        var stopped = false;   // a statement of the last bounded gather reached its bound

        // 2.9: a word whose own matches (the word and its stem) are already more than the RankLimit is answered from those
        // alone, without the substring recall of the trigram table. Listing the substring matches of a long common word costs
        // several times the listing of its own matches (measured on 127,000 mail texts: 65 ms against 10 ms for a word in
        // 19,000 of them) and adds a handful of documents; like the sound-alikes of 2.6.1 they are noise for such a word.
        if (options.RankLimit is { } ownLimit && tokens.Count > 0 && useTrigram)
        {
            (primary, secondary) = Gather(ranked: false, phonetic: false, substrings: false);
            if (primary.Count + secondary.Count > Math.Max(0, ownLimit))
            {
                return Unranked();
            }
        }

        if (options.ListOnly)
        {
            // 2.8: the caller wants the matches and no rank: they are listed, with the sound-alikes unless the word by itself is
            // over a RankLimit (the rule of 2.6.1, so that the documents are those of the ranked search).
            (primary, secondary) = Gather(ranked: false, phonetic: false);
            if (tokens.Count > 0 && _options.EnablePhonetic && options.EnablePhonetic
                && (options.RankLimit is not { } listLimit || primary.Count + secondary.Count <= Math.Max(0, listLimit)))
            {
                (primary, secondary) = Gather(ranked: false, phonetic: true);
            }

            return Unranked();
        }
        else if (options.RankLimit is { } rankLimit)
        {
            // 2.6.1: first without the sound-alikes. A word that is itself in more documents than the limit is answered from its own
            // matches; the phonetic clause made that listing up to twice as slow and added only noise to such a word.
            var soundAlikes = tokens.Count > 0 && _options.EnablePhonetic && options.EnablePhonetic;

            // 2.10: the matches are first gathered with their ranks, every statement stopped one row past the limit and
            // unordered, so its cost is bounded by the limit whatever the query matches. When no statement was stopped and the
            // matches together are within the limit, they are the ranked answer: the listings below would only have counted
            // them, each paying the trigram listing again (measured on 127,000 mail texts for a word in 1,000 of them: 17 ms
            // per listing and 30 ms ranked, so 64 ms became 30).
            var bound = Math.Max(0, rankLimit) + 1;
            var within = false;
            (primary, secondary) = (new Dictionary<long, double>(), new Dictionary<long, double>());
            if (bound <= options.CandidatePoolSize)
            {
                (primary, secondary) = Gather(ranked: true, phonetic: true, substrings: true, stopAt: bound);
                within = !stopped && primary.Count + secondary.Count < bound;
            }

            if (!within)
            {
                (primary, secondary) = Gather(ranked: false, phonetic: false);
                if (primary.Count + secondary.Count > Math.Max(0, rankLimit))
                {
                    return Unranked();
                }

                if (soundAlikes)
                {
                    (primary, secondary) = Gather(ranked: false, phonetic: true);
                    if (primary.Count + secondary.Count > Math.Max(0, rankLimit))
                    {
                        return Unranked();
                    }
                }

                if (primary.Count + secondary.Count > 0)
                {
                    (primary, secondary) = Gather(ranked: true, phonetic: true);
                }
            }
        }
        else
        {
            (primary, secondary) = Gather(ranked: true, phonetic: true);
        }

        List<RawHit> Unranked()
        {
            var rowids = primary.Keys.Order().Concat(secondary.Keys.Order()).Take(Math.Max(0, options.CandidatePoolSize)).ToList();
            var keys = keysOnly ? LoadDocumentIdsInParts(connection, rowids) : LoadDocumentKeys(connection, rowids);
            var listed = new List<RawHit>(rowids.Count);
            foreach (var rowid in rowids.Skip(Math.Max(0, options.Offset)).Take(Math.Max(0, options.Limit)))
            {
                if (keys.TryGetValue(rowid, out var key))
                {
                    listed.Add(new RawHit(key.DocId, key.MetadataJson, 0, null));
                }
            }

            return listed;
        }

        (Dictionary<long, double> Primary, Dictionary<long, double> Secondary) Gather(bool ranked, bool phonetic, bool substrings = true, int? stopAt = null)
        {
        stopped = false;
        var rows = stopAt ?? options.CandidatePoolSize;
        void Note(int given)
        {
            if (stopAt is { } at && given >= at)
            {
                Volatile.Write(ref stopped, true);
            }
        }

        var useTrigram = _options.EnableTrigram && query.Length >= 3 && substrings;
        var primary = new Dictionary<long, double>();
        var secondary = new Dictionary<long, double>();

        // §9.6: the escaped full query string against the trigram table (substring recall). It runs beside the token queries on a
        // connection of its own (2.5): ranking every match of a common word costs about as much in either table, and a query that
        // has tokens used to pay the two one after the other.
        Task<Dictionary<long, double>>? trigram = null;
        if (useTrigram && tokens.Count > 0)
        {
            trigram = Task.Run(() =>
            {
                using var secondSide = SqliteConnectionFactory.OpenSide(_connectionString);
                var second = secondSide.Connection;
                var found = new Dictionary<long, double>();
                Note(CollectCandidates(second, "documents_trgm", Fts5Match.EscapeToken(query), rows, found, ranked, stopAt is null));
                return found;
            });
        }

        try
        {
            GatherByTokens();
        }
        catch
        {
            // The trigram query must not outlive a failed search unobserved.
            if (trigram is not null)
            {
                try { trigram.GetAwaiter().GetResult(); }
                catch { }
            }

            throw;
        }

        void GatherByTokens()
        {
        if (tokens.Count > 0)
        {
            // §9.2: resolve the query language and pick its analyzer (or the identity fallback).
            var language = options.Language ?? _options.LanguageDetector?.Detect(query) ?? _options.DefaultLanguage;
            var analyzer = Analysis.Resolve(_options.Analyzers, language);

            // §9.3: derive qStem/qPhon with the same processing as content.
            var stemTokens = _options.EnableStemming
                ? Analysis.StemTokens(analyzer, tokens, _options.RemoveStopWords)
                : null;
            var phoneticTokens = phonetic && _options.EnablePhonetic && options.EnablePhonetic
                ? Analysis.PhoneticTokens(analyzer, tokens, _options.RemoveStopWords)
                : null;

            if (useAllTerms)
            {
                // Strict pass: implicit-AND on base only (cheap — the intersection is small).
                var andMatch = Fts5Match.BuildMatch(tokens, prefixLastToken: false, stemTokens: null, phoneticTokens: null, allTermsBase: true);
                Note(CollectCandidates(connection, "documents_fts", andMatch, rows, primary, ranked, stopAt is null));

                if (primary.Count < Math.Max(0, options.Limit))
                {
                    // Recall fallback: the classic §9.5 any-term expression incl. stem/phonetic.
                    var orMatch = Fts5Match.BuildMatch(tokens, prefixLastToken: query.Length < 3, stemTokens, phoneticTokens);
                    Note(CollectCandidates(connection, "documents_fts", orMatch, rows, secondary, ranked, stopAt is null));
                }
            }
            else
            {
                // §9.5: OR-combined column clauses, plus the short-query prefix aid on base when
                // the whole query is < 3 chars.
                var match = Fts5Match.BuildMatch(tokens, prefixLastToken: query.Length < 3, stemTokens, phoneticTokens);
                Note(CollectCandidates(connection, "documents_fts", match, rows, primary, ranked, stopAt is null));
            }
        }

        }

        if (useTrigram)
        {
            // In all-terms mode substring matches join the fallback tier unless the document also
            // satisfied the strict pass.
            var target = useAllTerms ? secondary : primary;
            if (trigram is null)
            {
                Note(CollectCandidates(connection, "documents_trgm", Fts5Match.EscapeToken(query), rows, target, ranked, stopAt is null));
            }
            else
            {
                foreach (var (rowid, rank) in trigram.GetAwaiter().GetResult())
                {
                    if (!target.TryGetValue(rowid, out var existing) || rank < existing)
                    {
                        target[rowid] = rank;
                    }
                }
            }
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

        return (primary, secondary);
        }

        if (primary.Count == 0 && secondary.Count == 0)
        {
            return NoRawHits;
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
            return NoRawHits;
        }

        // §9.8: load metadata, rank_text and content (if stored) for the candidates that are scored against their text: the whole
        // pool, or its best ScorePoolSize (by tier, then bm25) when that is set. The rest of the pool (the tail) is still returned,
        // with its bm25 share as the score, but its text is never loaded, folded or fuzzy-scored: that work grows with the pool and
        // with the size of the stored texts, and a caller that only needs to know which documents match does not want to pay it.
        var scoreCount = options.ScorePoolSize is { } scorePool ? Math.Clamp(scorePool, 0, pool.Count) : pool.Count;

        // A long tail is read on a connection of its own while the best candidates are loaded and scored (2.5.1).
        var tailRowids = scoreCount >= pool.Count ? null : pool.Skip(scoreCount).Select(candidate => candidate.Rowid).ToList();
        Dictionary<long, (string DocId, string? MetadataJson)> LoadTail(SqliteConnection from) =>
            keysOnly ? LoadDocumentIds(from, tailRowids!) : LoadDocumentKeys(from, tailRowids!);
        Task<Dictionary<long, (string DocId, string? MetadataJson)>>? tailLoad = null;
        if (tailRowids is { Count: >= ParallelTailFrom })
        {
            tailLoad = Task.Run(() =>
            {
                using var secondSide = SqliteConnectionFactory.OpenSide(_connectionString);
                var second = secondSide.Connection;
                return LoadTail(second);
            });
        }

        Dictionary<long, (string DocId, string MetadataJson, string RankText, string? Content)> documents;
        try
        {
            documents = LoadDocuments(connection, pool.Take(scoreCount).Select(candidate => candidate.Rowid));
        }
        catch
        {
            // The tail read must not outlive a failed search unobserved.
            if (tailLoad is not null)
            {
                try { tailLoad.GetAwaiter().GetResult(); }
                catch { }
            }

            throw;
        }

        // §9.9: normalize bm25 across the pool (lower is better; best maps to 1.0).
        var min = pool[0].Rank;
        var max = pool[0].Rank;
        foreach (var candidate in pool)
        {
            min = Math.Min(min, candidate.Rank);
            max = Math.Max(max, candidate.Rank);
        }

        var foldedQuery = TextFold.Fold(query);

        // §9.10/§9.11: the fuzzy pass and the exact-match boost read only the candidate's own text, so from a few dozen candidates
        // on they run on every core (2.5); the result is the same in any order.
        var textScores = new double[scoreCount];
        void ScoreText(int i)
        {
            var (rowid, rank, _) = pool[i];
            if (!documents.TryGetValue(rowid, out var doc))
            {
                textScores[i] = double.NaN;
                return;
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

            textScores[i] = score;
        }

        if (scoreCount >= ParallelScoreFrom)
        {
            Parallel.For(0, scoreCount, ScoreText);
        }
        else
        {
            for (var i = 0; i < scoreCount; i++)
            {
                ScoreText(i);
            }
        }

        var tailDocuments = tailRowids is null
            ? null
            : tailLoad is null ? LoadTail(connection) : tailLoad.GetAwaiter().GetResult();

        var scored = new List<(int Tier, double Score, string DocId, string? MetadataJson, string? Content)>(pool.Count);
        for (var i = 0; i < pool.Count; i++)
        {
            var (rowid, rank, tier) = pool[i];
            var normBm25 = max > min ? (max - rank) / (max - min) : 1.0;

            if (i >= scoreCount)
            {
                // The tail: a lexical match that is not scored against its text. Its score is the bm25 share of the blend, so it
                // sorts behind the scored candidates of its tier; MinScore does not apply to it.
                if (tailDocuments!.TryGetValue(rowid, out var key))
                {
                    scored.Add((tier, 0.4 * normBm25, key.DocId, key.MetadataJson, null));
                }

                continue;
            }

            var score = textScores[i];
            if (double.IsNaN(score) || !documents.TryGetValue(rowid, out var doc))
            {
                continue;
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
            .Take(Math.Max(0, options.Limit))
            .ToList();

        // §9.13: snippets are built only for the page; each reads only its own text, so a long page builds them on every core (2.5.1).
        var includeSnippet = options.IncludeSnippet && _options.StoreContent;
        var snippets = new string?[page.Count];
        if (includeSnippet)
        {
            void Snip(int i)
            {
                if (page[i].Content is { } content)
                {
                    snippets[i] = SnippetBuilder.Build(content, tokens, options.Snippet);
                }
            }

            if (page.Count >= ParallelScoreFrom)
            {
                Parallel.For(0, page.Count, Snip);
            }
            else
            {
                for (var i = 0; i < page.Count; i++)
                {
                    Snip(i);
                }
            }
        }

        var hits = new List<RawHit>(page.Count);
        for (var i = 0; i < page.Count; i++)
        {
            hits.Add(new RawHit(page[i].DocId, page[i].MetadataJson, page[i].Score, snippets[i]));
        }

        return hits;
    }

    /// <summary>
    /// Loads only <c>doc_id</c> for candidate rowids, from the slim <c>documents_key</c> index in one statement (the rowids travel
    /// as one JSON array): the rows of <c>documents</c>, which hold the stored texts, are not read.
    /// </summary>
    private static Dictionary<long, (string DocId, string? MetadataJson)> LoadDocumentIds(SqliteConnection connection, IEnumerable<long> rowids)
    {
        var json = new System.Text.StringBuilder("[");
        foreach (var rowid in rowids)
        {
            if (json.Length > 1)
            {
                json.Append(',');
            }

            json.Append(rowid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        json.Append(']');
        var documents = new Dictionary<long, (string, string?)>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT d.rowid, d.doc_id FROM json_each(@rowids) j CROSS JOIN documents d INDEXED BY documents_key ON d.rowid = j.value;";
        command.Parameters.AddWithValue("@rowids", json.ToString());
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            documents[reader.GetInt64(0)] = (reader.GetString(1), null);
        }

        return documents;
    }

    /// <summary>A list of rowids is read in parts from this many on (2.6.1).</summary>
    private const int KeyPartSize = 4096;

    /// <summary>
    /// <see cref="LoadDocumentIds"/> for a long list (2.6.1): the list is cut into parts that are read side by side, each on a
    /// connection of its own. Reading the ids of 50,000 listed matches in one statement was the largest part of an unranked query.
    /// </summary>
    private Dictionary<long, (string DocId, string? MetadataJson)> LoadDocumentIdsInParts(SqliteConnection connection, List<long> rowids)
    {
        var parts = Math.Min(Math.Min(Environment.ProcessorCount, 8), rowids.Count / KeyPartSize);
        if (parts < 2)
        {
            return LoadDocumentIds(connection, rowids);
        }

        var size = (rowids.Count + parts - 1) / parts;
        var others = new Task<Dictionary<long, (string DocId, string? MetadataJson)>>[parts - 1];
        for (var i = 1; i < parts; i++)
        {
            var part = rowids.GetRange(i * size, Math.Min(size, rowids.Count - i * size));
            others[i - 1] = Task.Run(() =>
            {
                using var ownSide = SqliteConnectionFactory.OpenSide(_connectionString);
                var own = ownSide.Connection;
                return LoadDocumentIds(own, part);
            });
        }

        Dictionary<long, (string DocId, string? MetadataJson)> all;
        try
        {
            all = LoadDocumentIds(connection, rowids.GetRange(0, size));
        }
        catch
        {
            // The other reads must not outlive a failed search unobserved.
            try { Task.WaitAll(others); }
            catch { }

            throw;
        }

        all.EnsureCapacity(rowids.Count);
        foreach (var other in others)
        {
            foreach (var (rowid, key) in other.GetAwaiter().GetResult())
            {
                all[rowid] = key;
            }
        }

        return all;
    }

    /// <summary>Loads only <c>doc_id</c> and metadata JSON for candidate rowids that are not scored against their text.</summary>
    private static Dictionary<long, (string DocId, string? MetadataJson)> LoadDocumentKeys(SqliteConnection connection, IEnumerable<long> rowids)
    {
        var documents = new Dictionary<long, (string, string?)>();
        foreach (var chunk in rowids.Chunk(500))
        {
            using var command = connection.CreateCommand();
            var names = new string[chunk.Length];
            for (var i = 0; i < chunk.Length; i++)
            {
                names[i] = "@r" + i;
                command.Parameters.AddWithValue(names[i], chunk[i]);
            }

            command.CommandText = $"SELECT rowid, doc_id, metadata FROM documents WHERE rowid IN ({string.Join(", ", names)});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                documents[reader.GetInt64(0)] = (reader.GetString(1), reader.GetString(2));
            }
        }

        return documents;
    }

    /// <summary>Runs one MATCH query and merges (rowid, bm25) results into <paramref name="bestRank"/>, keeping the lowest bm25 per rowid.</summary>
    /// <summary>Reads the matches of one statement into <paramref name="bestRank"/> and returns how many rows it gave. A ranked
    /// statement that is not <paramref name="ordered"/> stops after <paramref name="poolSize"/> rows in the order the index
    /// lists them: its cost is bounded by that number, and its rows are all the matches only when it gave fewer.</summary>
    private static int CollectCandidates(
        SqliteConnection connection,
        string table,
        string match,
        int poolSize,
        Dictionary<long, double> bestRank,
        bool ranked,
        bool ordered = true)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ranked
            ? ordered
                ? $"SELECT rowid, bm25({table}) AS rank FROM {table} WHERE {table} MATCH @match ORDER BY rank, rowid LIMIT @pool;"
                : $"SELECT rowid, bm25({table}) AS rank FROM {table} WHERE {table} MATCH @match LIMIT @pool;"
            : $"SELECT rowid, 0.0 AS rank FROM {table} WHERE {table} MATCH @match LIMIT @pool;";
        command.Parameters.AddWithValue("@match", match);
        command.Parameters.AddWithValue("@pool", poolSize);

        var given = 0;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            given++;
            var rowid = reader.GetInt64(0);
            var rank = reader.GetDouble(1);
            if (!bestRank.TryGetValue(rowid, out var existing) || rank < existing)
            {
                bestRank[rowid] = rank;
            }
        }

        return given;
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
                using var connectionSide = SqliteConnectionFactory.OpenSide(_connectionString);
                var connection = connectionSide.Connection;
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
