using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using ReperioNet.Abstractions;

namespace ReperioNet;

/// <summary>Configuration for a <see cref="SearchIndex{TMeta}"/>, supplied via the <c>configure</c> callback of <see cref="SearchIndex{TMeta}.OpenAsync"/>.</summary>
/// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
public sealed class ReperioOptions<TMeta>
{
    /// <summary>
    /// Initializes the options with the default analyzer provider and fuzzy ranker.
    /// <see cref="MetadataTypeInfo"/> starts unset and must be assigned by the caller.
    /// </summary>
    [SetsRequiredMembers]
    internal ReperioOptions()
    {
        MetadataTypeInfo = null!;
    }

    /// <summary>The analyzer registry; language packs register their analyzers here.</summary>
    public IAnalyzerProvider Analyzers { get; } = new DefaultAnalyzerProvider();

    /// <summary>Optional language detector used when an entry or query has no explicit language.</summary>
    public ILanguageDetector? LanguageDetector { get; set; }

    /// <summary>Fallback ISO 639-1 code used when detection is off or uncertain.</summary>
    public string? DefaultLanguage { get; set; }

    /// <summary>The fuzzy re-ranker. Default: <see cref="TokenSetFuzzyRanker"/> (FuzzySharp token-set ratio).</summary>
    public IFuzzyRanker FuzzyRanker { get; set; } = new TokenSetFuzzyRanker();

    /// <summary>Stores one copy of each entry's content (enables snippets and full fuzzy re-ranking). Default: <see langword="true"/>.</summary>
    public bool StoreContent { get; set; } = true;

    /// <summary>Creates and maintains the trigram index for substring/typo recall. Default: <see langword="true"/>.</summary>
    public bool EnableTrigram { get; set; } = true;

    /// <summary>Populates and queries the stem column. Default: <see langword="true"/>.</summary>
    public bool EnableStemming { get; set; } = true;

    /// <summary>Populates and queries the phonetic column. Default: <see langword="true"/>.</summary>
    public bool EnablePhonetic { get; set; } = true;

    /// <summary>Drops stop words from the stem/phonetic streams (never from base). Default: <see langword="false"/>.</summary>
    public bool RemoveStopWords { get; set; }

    /// <summary>Truncates indexed text to this many characters; 0 = unbounded. Default: 0.</summary>
    public int MaxContentChars { get; set; }

    /// <summary>
    /// Optional 32-byte key that encrypts the index file and its write-ahead log with the default cipher of
    /// SQLite3 Multiple Ciphers (ChaCha20-Poly1305). It needs the process to run on that SQLite build
    /// (package <c>SQLite3MC.PCLRaw.bundle</c> instead of <c>SQLitePCLRaw.bundle_e_sqlite3</c>); on any other build
    /// <see cref="SearchIndex{TMeta}.OpenAsync"/> throws <see cref="ReperioException"/> before anything is written.
    /// The same key must be given on every open: a wrong key throws <see cref="ReperioException"/>. A key that is not
    /// exactly 32 bytes throws <see cref="ArgumentException"/>. Default: <see langword="null"/> (no encryption).
    /// </summary>
    public byte[]? EncryptionKey { get; set; }

    /// <summary>The structured fields (keys, numbers, flags) maintained for filtering, grouping and ordering (PRD §4a).</summary>
    public FieldDeclarations Fields { get; } = new();

    /// <summary>
    /// REQUIRED: source-generated <see cref="JsonTypeInfo{TMeta}"/> used to serialize metadata
    /// (AOT/trimming-safe; no reflection fallback exists). <see cref="SearchIndex{TMeta}.OpenAsync"/>
    /// throws <see cref="ReperioException"/> if this is not set.
    /// </summary>
    public required JsonTypeInfo<TMeta> MetadataTypeInfo { get; set; }
}
