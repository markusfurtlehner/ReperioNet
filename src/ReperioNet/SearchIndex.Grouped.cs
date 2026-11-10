using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ReperioNet;

public sealed partial class SearchIndex<TMeta>
{
    private const int MaxPageSize = 1000;

    /// <summary>
    /// Lists the groups of a keys field that have at least one member matching the query filter,
    /// newest matching member first, keyset-paged (PRD §4a).
    /// </summary>
    /// <param name="query">The group query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">Undeclared or wrongly typed field, a group field without an
    /// <c>orderBy</c>, a page size outside 1..1000 or a malformed cursor.</exception>
    public Task<GroupPage> GroupsAsync(GroupQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfDisposed();
        return RunReadAsync(connection => GroupsCore(connection, query), ct);
    }

    /// <summary>
    /// Lists the members of one group key matching the query filter, newest first, keyset-paged (PRD §4a).
    /// </summary>
    /// <param name="query">The member query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">Undeclared or wrongly typed field, a group field without an
    /// <c>orderBy</c>, a page size outside 1..1000 or a malformed cursor.</exception>
    public Task<MemberPage<TMeta>> MembersAsync(MemberQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfDisposed();
        return RunReadAsync(connection => MembersCore(connection, query), ct);
    }

    private GroupPage GroupsCore(SqliteConnection connection, GroupQuery query)
    {
        RequireOrderedKeysField(query.GroupField);
        RequirePageSize(query.PageSize);
        var flags = query.CountWhere.ToArray();
        foreach (var flag in flags)
        {
            if (!_options.Fields.TryGet(flag, out var declared) || declared.Kind != FieldKind.Flag)
            {
                throw new ArgumentException($"CountWhere field '{flag}' is not a declared Flag field.", nameof(query));
            }
        }

        var cursor = query.After is null ? null : Cursor.Parse(query.After, 'g');
        var filter = CompileFilter(query.Filter, "k.rowid");
        var rowFilter = CompileFilter(query.Filter, "@r");

        // Walk the (order, row, key) pairs of the field from the cursor in the total order, without
        // the filter. The first pair of a group that passes the filter is a candidate: it is the
        // group's newest matching member iff no newer member of that group matches. Every later
        // pair of that group is then irrelevant, so each group is judged once per page.
        var walkSql = new StringBuilder("SELECT k.ord, k.rowid, k.key FROM doc_keys k INDEXED BY doc_keys_walk WHERE k.field = @gf");
        if (cursor is not null)
        {
            walkSql.Append(" AND k.ord <= @co AND (k.ord < @co OR k.rowid <= @cr)")
                .Append(" AND NOT (k.ord = @co AND k.rowid = @cr AND k.key <= @ck)");
        }

        walkSql.Append(" ORDER BY k.ord DESC, k.rowid DESC, k.key ASC;");

        var matchSql = "SELECT " + rowFilter.Sql + ";";
        var newerSql =
            "SELECT 1 FROM doc_keys k INDEXED BY doc_keys_member WHERE k.field = @gf AND k.key = @key " +
            "AND k.ord >= @o AND (k.ord > @o OR k.rowid > @r) AND " + filter.Sql +
            " LIMIT 1;"; // existence only: no ORDER BY, the scan starts next to the candidate

        var aggregate = new StringBuilder("SELECT COUNT(*)");
        for (var i = 0; i < flags.Length; i++)
        {
            aggregate.Append(", COALESCE(SUM(EXISTS(SELECT 1 FROM doc_nums a").Append(i)
                .Append(" WHERE a").Append(i).Append(".rowid = k.rowid AND a").Append(i)
                .Append(".field = @af").Append(i).Append(" AND a").Append(i).Append(".value = 1)), 0)");
        }

        aggregate.Append(" FROM doc_keys k WHERE k.field = @gf AND k.key = @key AND ").Append(filter.Sql).Append(';');

        using var snapshot = connection.BeginTransaction();
        using var walk = Command(connection, snapshot, walkSql.ToString(), null);
        using var match = Command(connection, snapshot, matchSql, rowFilter);
        using var newer = Command(connection, snapshot, newerSql, filter);
        using var count = Command(connection, snapshot, aggregate.ToString(), filter);
        using var idLookup = Command(connection, snapshot, "SELECT doc_id FROM documents WHERE rowid = @r;", null);
        walk.Parameters.AddWithValue("@gf", query.GroupField);
        newer.Parameters.Add("@gf", SqliteType.Text).Value = query.GroupField;
        count.Parameters.Add("@gf", SqliteType.Text).Value = query.GroupField;
        for (var i = 0; i < flags.Length; i++)
        {
            count.Parameters.Add("@af" + i, SqliteType.Text).Value = flags[i];
        }

        if (cursor is not null)
        {
            walk.Parameters.AddWithValue("@co", cursor.Order);
            walk.Parameters.AddWithValue("@cr", cursor.Row);
            walk.Parameters.AddWithValue("@ck", cursor.Key);
        }

        var matchRow = match.Parameters.Add("@r", SqliteType.Integer);
        var newerKey = newer.Parameters.Add("@key", SqliteType.Text);
        var newerOrd = newer.Parameters.Add("@o", SqliteType.Integer);
        var newerRow = newer.Parameters.Add("@r", SqliteType.Integer);
        var countKey = count.Parameters.Add("@key", SqliteType.Text);
        var idRow = idLookup.Parameters.Add("@r", SqliteType.Integer);

        // Executed once per candidate group: prepare once, reuse for the whole page.
        match.Prepare();
        newer.Prepare();
        count.Prepare();
        idLookup.Prepare();

        var groups = new List<GroupSummary>(Math.Min(query.PageSize, 64));
        var judged = new HashSet<string>(StringComparer.Ordinal);
        (long Order, long Row, string Key) last = (0, 0, string.Empty);
        string? next = null;
        using var reader = walk.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(2);
            if (judged.Contains(key))
            {
                continue;
            }

            var ord = reader.GetInt64(0);
            var row = reader.GetInt64(1);

            matchRow.Value = row;
            if (Convert.ToInt64(match.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                continue; // this member is filtered out; an older one may still make the group count
            }

            judged.Add(key);
            newerKey.Value = key;
            newerOrd.Value = ord;
            newerRow.Value = row;
            if (newer.ExecuteScalar() is not null)
            {
                continue; // a newer matching member exists: the group sits above this position
            }

            if (groups.Count == query.PageSize)
            {
                next = new Cursor('g', last.Order, last.Row, last.Key).Encode();
                break;
            }

            countKey.Value = key;
            long total;
            var flagCounts = new Dictionary<string, long>(flags.Length, StringComparer.Ordinal);
            using (var counts = count.ExecuteReader())
            {
                counts.Read();
                total = counts.GetInt64(0);
                for (var i = 0; i < flags.Length; i++)
                {
                    flagCounts[flags[i]] = counts.GetInt64(i + 1);
                }
            }

            idRow.Value = row;
            var newestId = (string)idLookup.ExecuteScalar()!;
            groups.Add(new GroupSummary(key, newestId, ord, total, flagCounts));
            last = (ord, row, key);
        }

        return new GroupPage(groups, next);
    }

    private MemberPage<TMeta> MembersCore(SqliteConnection connection, MemberQuery query)
    {
        RequireOrderedKeysField(query.GroupField);
        RequirePageSize(query.PageSize);
        ArgumentException.ThrowIfNullOrEmpty(query.GroupKey);
        var cursor = query.After is null ? null : Cursor.Parse(query.After, 'm');
        var filter = CompileFilter(query.Filter, "k.rowid");

        var sql = new StringBuilder(
            "SELECT k.ord, k.rowid, d.doc_id, d.metadata FROM doc_keys k INDEXED BY doc_keys_member " +
            "JOIN documents d ON d.rowid = k.rowid WHERE k.field = @gf AND k.key = @key");
        if (cursor is not null)
        {
            sql.Append(" AND k.ord <= @co AND (k.ord < @co OR k.rowid < @cr)");
        }

        sql.Append(" AND ").Append(filter.Sql).Append(" ORDER BY k.ord DESC, k.rowid DESC LIMIT @n;");

        using var snapshot = connection.BeginTransaction();
        using var command = Command(connection, snapshot, sql.ToString(), filter);
        command.Parameters.AddWithValue("@gf", query.GroupField);
        command.Parameters.AddWithValue("@key", query.GroupKey);
        command.Parameters.AddWithValue("@n", (long)query.PageSize + 1);
        if (cursor is not null)
        {
            command.Parameters.AddWithValue("@co", cursor.Order);
            command.Parameters.AddWithValue("@cr", cursor.Row);
        }

        var items = new List<MemberHit<TMeta>>(Math.Min(query.PageSize, 64));
        string? next = null;
        long lastOrd = 0;
        long lastRow = 0;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (items.Count == query.PageSize)
            {
                next = new Cursor('m', lastOrd, lastRow, string.Empty).Encode();
                break;
            }

            lastOrd = reader.GetInt64(0);
            lastRow = reader.GetInt64(1);
            var metadata = JsonSerializer.Deserialize(reader.GetString(3), _options.MetadataTypeInfo)!;
            items.Add(new MemberHit<TMeta>(reader.GetString(2), metadata, lastOrd));
        }

        return new MemberPage<TMeta>(items, next);
    }

    private void RequireOrderedKeysField(string field)
    {
        if (!_options.Fields.TryGet(field, out var declared) || declared.Kind != FieldKind.Keys)
        {
            throw new ArgumentException($"Group field '{field}' is not a declared Keys field.");
        }

        if (declared.OrderBy is null)
        {
            throw new ArgumentException($"Keys field '{field}' was declared without an orderBy number and cannot be used for grouped queries.");
        }
    }

    private static void RequirePageSize(int pageSize)
    {
        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new ArgumentException($"PageSize must be between 1 and {MaxPageSize}.");
        }
    }

    private (string Sql, List<(string Name, object Value)> Parameters) CompileFilter(FieldFilter? filter, string rowid)
    {
        if (filter is null)
        {
            return ("1", []);
        }

        var writer = new SqlFilterWriter(_options.Fields);
        filter.Write(writer, rowid);
        return (writer.Sql.ToString(), writer.Parameters);
    }

    private static SqliteCommand Command(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        (string Sql, List<(string Name, object Value)> Parameters)? filter)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (filter is { } f)
        {
            foreach (var (name, value) in f.Parameters)
            {
                command.Parameters.Add(name, value is long ? SqliteType.Integer : SqliteType.Text).Value = value;
            }
        }

        return command;
    }

    /// <summary>The opaque keyset position: <c>(order, row[, key])</c>.</summary>
    private sealed record Cursor(char Kind, long Order, long Row, string Key)
    {
        internal string Encode()
        {
            var text = string.Create(
                CultureInfo.InvariantCulture, $"{Kind}1|{Order}|{Row}|{Key}");
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        internal static Cursor Parse(string text, char kind)
        {
            try
            {
                var padded = text.Replace('-', '+').Replace('_', '/');
                padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|', 4);
                if (parts.Length == 4 && parts[0] == kind + "1")
                {
                    return new Cursor(
                        kind,
                        long.Parse(parts[1], CultureInfo.InvariantCulture),
                        long.Parse(parts[2], CultureInfo.InvariantCulture),
                        parts[3]);
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
            {
                // fall through to the uniform error below
            }

            throw new ArgumentException("The cursor is malformed or belongs to another kind of query.", nameof(text));
        }
    }
}
