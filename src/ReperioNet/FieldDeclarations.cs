namespace ReperioNet;

/// <summary>The kind of an indexed field (PRD §4a).</summary>
public enum FieldKind
{
    /// <summary>Multi-valued keyword field (group keys, accounts, folder roles, ...).</summary>
    Keys,

    /// <summary>Single-valued signed 64-bit number (dates as UTC ticks, sizes, ...).</summary>
    Number,

    /// <summary>Single-valued boolean.</summary>
    Flag,
}

/// <summary>
/// The structured fields an index maintains beside the full-text tables (PRD §4a). Declared on
/// <see cref="ReperioOptions{TMeta}.Fields"/> and persisted in <c>reperio_meta</c>; reopening with a
/// different declaration is a layout mismatch.
/// </summary>
public sealed class FieldDeclarations
{
    private readonly SortedDictionary<string, (FieldKind Kind, string? OrderBy)> _fields = new(StringComparer.Ordinal);

    internal FieldDeclarations()
    {
    }

    /// <summary>
    /// Declares a multi-valued keyword field. With <paramref name="orderBy"/> (a declared
    /// <see cref="Number"/> field) the field can be used for grouped and member queries, ordered by
    /// that number descending.
    /// </summary>
    public FieldDeclarations Keys(string name, string? orderBy = null)
    {
        if (orderBy is not null)
        {
            ValidateName(orderBy);
        }

        return Add(name, FieldKind.Keys, orderBy);
    }

    /// <summary>Declares a single-valued number field.</summary>
    public FieldDeclarations Number(string name) => Add(name, FieldKind.Number, null);

    /// <summary>Declares a single-valued boolean field (an absent value counts as false).</summary>
    public FieldDeclarations Flag(string name) => Add(name, FieldKind.Flag, null);

    internal IReadOnlyDictionary<string, (FieldKind Kind, string? OrderBy)> All => _fields;

    internal bool TryGet(string name, out (FieldKind Kind, string? OrderBy) declaration)
        => _fields.TryGetValue(name, out declaration);

    /// <summary>Canonical text persisted in <c>reperio_meta</c>.</summary>
    internal string Canonical()
        => string.Join(
            ';',
            _fields.Select(f => $"{f.Value.Kind.ToString().ToLowerInvariant()}:{f.Key}" +
                                (f.Value.OrderBy is null ? string.Empty : $">{f.Value.OrderBy}")));

    /// <summary>Checks that every ordering references a declared number field.</summary>
    internal void Validate()
    {
        foreach (var (name, (kind, orderBy)) in _fields)
        {
            if (kind == FieldKind.Keys && orderBy is not null &&
                !(_fields.TryGetValue(orderBy, out var target) && target.Kind == FieldKind.Number))
            {
                throw new ReperioException(
                    $"Keys field '{name}' is ordered by '{orderBy}', which is not a declared Number field.");
            }
        }
    }

    internal static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
        {
            throw new ArgumentException(
                "A field name must be non-empty and contain only ASCII letters, digits, '_', '-' or '.'.",
                nameof(name));
        }
    }

    private FieldDeclarations Add(string name, FieldKind kind, string? orderBy)
    {
        ValidateName(name);
        if (_fields.ContainsKey(name))
        {
            throw new ArgumentException($"Field '{name}' is already declared.", nameof(name));
        }

        _fields[name] = (kind, orderBy);
        return this;
    }
}
