using ReperioNet.Abstractions;

namespace ReperioNet.Languages.No;

/// <summary>
/// Norwegian (<c>"no"</c>) language analyzer: Snowball Norwegian (Bokmål) stemmer plus a curated
/// stop-word list. Norwegian has no standard phonetic encoder, so <see cref="Phonetic"/> is
/// <see langword="null"/>.
/// </summary>
public sealed class NorwegianAnalyzer : ILanguageAnalyzer
{
    /// <summary>The ISO 639-1 code for Norwegian: <c>"no"</c>.</summary>
    public string LanguageCode => "no";

    /// <summary>The Snowball Norwegian stemmer.</summary>
    public IStemmer Stemmer { get; } = new SnowballNorwegianStemmer();

    /// <summary>Always <see langword="null"/>: no phonetic encoding is standard for Norwegian.</summary>
    public IPhoneticEncoder? Phonetic => null;

    /// <summary>The Norwegian stop-word filter.</summary>
    public IStopWordFilter? StopWords { get; } = new NorwegianStopWords();
}
