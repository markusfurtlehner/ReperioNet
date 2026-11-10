using System.Text;

namespace ReperioNet;

/// <summary>
/// A predicate over the structured fields of a document (PRD §4a), used by
/// <see cref="SearchIndex{TMeta}.GroupsAsync"/> and <see cref="SearchIndex{TMeta}.MembersAsync"/>.
/// Build with the static factories and combine with <see cref="And"/>, <see cref="Or"/>, <see cref="Not"/>.
/// </summary>
public abstract class FieldFilter
{
    internal FieldFilter()
    {
    }

    /// <summary>The document has <paramref name="key"/> in the keys field.</summary>
    public static FieldFilter KeyEquals(string field, string key) => new KeyInFilter(field, [key], negate: false);

    /// <summary>The document has at least one of <paramref name="keys"/> in the keys field.</summary>
    public static FieldFilter KeyIn(string field, params string[] keys) => new KeyInFilter(field, keys, negate: false);

    /// <summary>The document has none of <paramref name="keys"/> in the keys field (true when it has no keys at all).</summary>
    public static FieldFilter KeyNotIn(string field, params string[] keys) => new KeyInFilter(field, keys, negate: true);

    /// <summary>The flag field equals <paramref name="value"/> (an absent flag is false).</summary>
    public static FieldFilter Flag(string field, bool value) => new FlagIs(field, value);

    /// <summary>The number field is at least <paramref name="min"/> (inclusive); an absent number never matches.</summary>
    public static FieldFilter NumberAtLeast(string field, long min) => new NumberRange(field, min, null);

    /// <summary>The number field is at most <paramref name="max"/> (inclusive); an absent number never matches.</summary>
    public static FieldFilter NumberAtMost(string field, long max) => new NumberRange(field, null, max);

    /// <summary>The number field lies in <paramref name="min"/>..<paramref name="max"/> (inclusive).</summary>
    public static FieldFilter NumberBetween(string field, long min, long max) => new NumberRange(field, min, max);

    /// <summary>All of <paramref name="filters"/> hold (true for none).</summary>
    public static FieldFilter And(params FieldFilter[] filters) => new Combine(filters, isAnd: true);

    /// <summary>At least one of <paramref name="filters"/> holds (false for none).</summary>
    public static FieldFilter Or(params FieldFilter[] filters) => new Combine(filters, isAnd: false);

    /// <summary>Negates <paramref name="filter"/>.</summary>
    public static FieldFilter Not(FieldFilter filter) => new Negation(filter);

    /// <summary>Appends SQL that is true for the document whose rowid expression is <paramref name="rowid"/>.</summary>
    internal abstract void Write(SqlFilterWriter writer, string rowid);

    private sealed class KeyInFilter(string field, string[] keys, bool negate) : FieldFilter
    {
        internal override void Write(SqlFilterWriter w, string rowid)
        {
            w.RequireKind(field, FieldKind.Keys);
            if (keys.Length == 0)
            {
                w.Sql.Append(negate ? "1" : "0");
                return;
            }

            var alias = w.NextAlias();
            w.Sql.Append(negate ? "NOT EXISTS" : "EXISTS")
                .Append("(SELECT 1 FROM doc_keys ").Append(alias)
                .Append(" WHERE ").Append(alias).Append(".rowid = ").Append(rowid)
                .Append(" AND ").Append(alias).Append(".field = ").Append(w.Param(field))
                .Append(" AND ").Append(alias).Append(".key IN (")
                .Append(string.Join(',', keys.Select(k => w.Param(k)))).Append("))");
        }
    }

    private sealed class FlagIs(string field, bool value) : FieldFilter
    {
        internal override void Write(SqlFilterWriter w, string rowid)
        {
            w.RequireKind(field, FieldKind.Flag);
            var alias = w.NextAlias();
            // An absent flag is false: "true" needs a value-1 row; "false" needs no value-1 row.
            w.Sql.Append(value ? "EXISTS" : "NOT EXISTS")
                .Append("(SELECT 1 FROM doc_nums ").Append(alias)
                .Append(" WHERE ").Append(alias).Append(".rowid = ").Append(rowid)
                .Append(" AND ").Append(alias).Append(".field = ").Append(w.Param(field))
                .Append(" AND ").Append(alias).Append(".value = 1)");
        }
    }

    private sealed class NumberRange(string field, long? min, long? max) : FieldFilter
    {
        internal override void Write(SqlFilterWriter w, string rowid)
        {
            w.RequireKind(field, FieldKind.Number);
            var alias = w.NextAlias();
            w.Sql.Append("EXISTS(SELECT 1 FROM doc_nums ").Append(alias)
                .Append(" WHERE ").Append(alias).Append(".rowid = ").Append(rowid)
                .Append(" AND ").Append(alias).Append(".field = ").Append(w.Param(field));
            if (min is { } lo)
            {
                w.Sql.Append(" AND ").Append(alias).Append(".value >= ").Append(w.Param(lo));
            }

            if (max is { } hi)
            {
                w.Sql.Append(" AND ").Append(alias).Append(".value <= ").Append(w.Param(hi));
            }

            w.Sql.Append(')');
        }
    }

    private sealed class Combine(FieldFilter[] parts, bool isAnd) : FieldFilter
    {
        internal override void Write(SqlFilterWriter w, string rowid)
        {
            if (parts.Length == 0)
            {
                w.Sql.Append(isAnd ? "1" : "0");
                return;
            }

            w.Sql.Append('(');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    w.Sql.Append(isAnd ? " AND " : " OR ");
                }

                ArgumentNullException.ThrowIfNull(parts[i]);
                parts[i].Write(w, rowid);
            }

            w.Sql.Append(')');
        }
    }

    private sealed class Negation(FieldFilter inner) : FieldFilter
    {
        internal override void Write(SqlFilterWriter w, string rowid)
        {
            ArgumentNullException.ThrowIfNull(inner);
            w.Sql.Append("NOT (");
            inner.Write(w, rowid);
            w.Sql.Append(')');
        }
    }
}

/// <summary>Collects the SQL text and bound parameters of a compiled <see cref="FieldFilter"/>.</summary>
internal sealed class SqlFilterWriter(FieldDeclarations declarations)
{
    private int _alias;
    private int _param;

    internal StringBuilder Sql { get; } = new();

    internal List<(string Name, object Value)> Parameters { get; } = [];

    internal string NextAlias() => "f" + _alias++;

    internal string Param(object value)
    {
        var name = "@fp" + _param++;
        Parameters.Add((name, value));
        return name;
    }

    internal void RequireKind(string field, FieldKind kind)
    {
        if (!declarations.TryGet(field, out var declared))
        {
            throw new ArgumentException($"Field '{field}' is not declared.");
        }

        if (declared.Kind != kind)
        {
            throw new ArgumentException($"Field '{field}' is a {declared.Kind} field, not {kind}.");
        }
    }
}
