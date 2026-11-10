namespace ReperioNet;

/// <summary>
/// The structured field values of one <see cref="SearchEntry{TMeta}"/> (PRD §4a). Every name must be
/// declared on <see cref="ReperioOptions{TMeta}.Fields"/> with the matching kind. An upsert replaces
/// all field values of the document; fields not set here are absent.
/// </summary>
public sealed class EntryFields
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    /// <summary>Sets the keys of a <see cref="FieldKind.Keys"/> field (duplicates collapse; keys must be non-empty).</summary>
    public EntryFields Keys(string name, params string[] keys) => Keys(name, (IEnumerable<string>)keys);

    /// <summary>Sets the keys of a <see cref="FieldKind.Keys"/> field.</summary>
    public EntryFields Keys(string name, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A key must be non-empty.", nameof(keys));
            }

            set.Add(key);
        }

        return Set(name, set);
    }

    /// <summary>Sets a <see cref="FieldKind.Number"/> field.</summary>
    public EntryFields Number(string name, long value) => Set(name, value);

    /// <summary>Sets a <see cref="FieldKind.Number"/> field to the UTC ticks of <paramref name="value"/>.</summary>
    public EntryFields Number(string name, DateTimeOffset value) => Set(name, value.UtcTicks);

    /// <summary>Sets a <see cref="FieldKind.Flag"/> field.</summary>
    public EntryFields Flag(string name, bool value) => Set(name, value);

    internal IReadOnlyDictionary<string, object> Values => _values;

    private EntryFields Set(string name, object value)
    {
        FieldDeclarations.ValidateName(name);
        _values[name] = value;
        return this;
    }
}
