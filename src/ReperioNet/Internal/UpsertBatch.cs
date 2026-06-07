using Microsoft.Data.Sqlite;

namespace ReperioNet.Internal;

/// <summary>
/// The two-step upsert of PRD §15.5 (binding), with prepared <see cref="SqliteCommand"/>s reused
/// across an entire batch (PRD §6.6). Keeps the internal rowid stable on update so the FTS rows stay
/// aligned; deliberately avoids <c>INSERT OR REPLACE</c>, which could change the rowid.
/// </summary>
internal sealed class UpsertBatch : IDisposable
{
    private readonly SqliteCommand _selectRowid;
    private readonly SqliteCommand _deleteFts;
    private readonly SqliteCommand? _deleteTrigram;
    private readonly SqliteCommand _updateDocument;
    private readonly SqliteCommand _insertDocument;
    private readonly SqliteCommand _insertFts;
    private readonly SqliteCommand? _insertTrigram;

    internal UpsertBatch(SqliteConnection connection, SqliteTransaction transaction, bool enableTrigram)
    {
        _selectRowid = Create(
            connection,
            transaction,
            "SELECT rowid FROM documents WHERE doc_id = @doc_id;",
            ("@doc_id", SqliteType.Text));

        _deleteFts = Create(
            connection,
            transaction,
            "DELETE FROM documents_fts WHERE rowid = @rowid;",
            ("@rowid", SqliteType.Integer));

        _updateDocument = Create(
            connection,
            transaction,
            """
            UPDATE documents
               SET language = @language, metadata = @metadata,
                   rank_text = @rank_text, content = @content
             WHERE rowid = @rowid;
            """,
            ("@language", SqliteType.Text),
            ("@metadata", SqliteType.Text),
            ("@rank_text", SqliteType.Text),
            ("@content", SqliteType.Text),
            ("@rowid", SqliteType.Integer));

        _insertDocument = Create(
            connection,
            transaction,
            """
            INSERT INTO documents (doc_id, language, metadata, rank_text, content)
            VALUES (@doc_id, @language, @metadata, @rank_text, @content);
            SELECT last_insert_rowid();
            """,
            ("@doc_id", SqliteType.Text),
            ("@language", SqliteType.Text),
            ("@metadata", SqliteType.Text),
            ("@rank_text", SqliteType.Text),
            ("@content", SqliteType.Text));

        _insertFts = Create(
            connection,
            transaction,
            "INSERT INTO documents_fts (rowid, base, stem, phonetic) VALUES (@rowid, @base, @stem, @phonetic);",
            ("@rowid", SqliteType.Integer),
            ("@base", SqliteType.Text),
            ("@stem", SqliteType.Text),
            ("@phonetic", SqliteType.Text));

        if (enableTrigram)
        {
            _deleteTrigram = Create(
                connection,
                transaction,
                "DELETE FROM documents_trgm WHERE rowid = @rowid;",
                ("@rowid", SqliteType.Integer));

            _insertTrigram = Create(
                connection,
                transaction,
                "INSERT INTO documents_trgm (rowid, text) VALUES (@rowid, @text);",
                ("@rowid", SqliteType.Integer),
                ("@text", SqliteType.Text));
        }
    }

    /// <summary>
    /// Upserts one document. Column values follow PRD §15.4: <paramref name="baseText"/> is the raw
    /// (truncated) content; <paramref name="stem"/>/<paramref name="phonetic"/> are empty strings in
    /// Milestones 1–2.
    /// </summary>
    internal void Upsert(
        string docId,
        string? language,
        string metadataJson,
        string rankText,
        string? content,
        string baseText,
        string stem,
        string phonetic)
    {
        // Step 1: find the existing internal rowid (NULL if new).
        _selectRowid.Parameters["@doc_id"].Value = docId;

        long rowid;
        if (_selectRowid.ExecuteScalar() is long existingRowid)
        {
            // Existing document: reuse the rowid, drop the old FTS rows, update in place.
            rowid = existingRowid;

            _deleteFts.Parameters["@rowid"].Value = rowid;
            _deleteFts.ExecuteNonQuery();

            if (_deleteTrigram is not null)
            {
                _deleteTrigram.Parameters["@rowid"].Value = rowid;
                _deleteTrigram.ExecuteNonQuery();
            }

            _updateDocument.Parameters["@language"].Value = (object?)language ?? DBNull.Value;
            _updateDocument.Parameters["@metadata"].Value = metadataJson;
            _updateDocument.Parameters["@rank_text"].Value = rankText;
            _updateDocument.Parameters["@content"].Value = (object?)content ?? DBNull.Value;
            _updateDocument.Parameters["@rowid"].Value = rowid;
            _updateDocument.ExecuteNonQuery();
        }
        else
        {
            _insertDocument.Parameters["@doc_id"].Value = docId;
            _insertDocument.Parameters["@language"].Value = (object?)language ?? DBNull.Value;
            _insertDocument.Parameters["@metadata"].Value = metadataJson;
            _insertDocument.Parameters["@rank_text"].Value = rankText;
            _insertDocument.Parameters["@content"].Value = (object?)content ?? DBNull.Value;
            rowid = (long)_insertDocument.ExecuteScalar()!;
        }

        _insertFts.Parameters["@rowid"].Value = rowid;
        _insertFts.Parameters["@base"].Value = baseText;
        _insertFts.Parameters["@stem"].Value = stem;
        _insertFts.Parameters["@phonetic"].Value = phonetic;
        _insertFts.ExecuteNonQuery();

        if (_insertTrigram is not null)
        {
            _insertTrigram.Parameters["@rowid"].Value = rowid;
            _insertTrigram.Parameters["@text"].Value = baseText;
            _insertTrigram.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        _selectRowid.Dispose();
        _deleteFts.Dispose();
        _deleteTrigram?.Dispose();
        _updateDocument.Dispose();
        _insertDocument.Dispose();
        _insertFts.Dispose();
        _insertTrigram?.Dispose();
    }

    private static SqliteCommand Create(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, SqliteType Type)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, type) in parameters)
        {
            command.Parameters.Add(name, type);
        }

        command.Prepare();
        return command;
    }
}
