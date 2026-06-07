using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Da;

/// <summary>
/// Danish (<c>"da"</c>) language analyzer: Snowball Danish stemmer plus a curated stop-word list.
/// Danish has no standard phonetic encoder, so <see cref="Phonetic"/> is <see langword="null"/>.
/// </summary>
public sealed class DanishAnalyzer : ILanguageAnalyzer
{
    /// <summary>The ISO 639-1 code for Danish: <c>"da"</c>.</summary>
    public string LanguageCode => "da";

    /// <summary>The Snowball Danish stemmer.</summary>
    public IStemmer Stemmer { get; } = new SnowballDanishStemmer();

    /// <summary>Always <see langword="null"/>: no phonetic encoding is standard for Danish.</summary>
    public IPhoneticEncoder? Phonetic => null;

    /// <summary>The Danish stop-word filter.</summary>
    public IStopWordFilter? StopWords { get; } = new DanishStopWords();
}
