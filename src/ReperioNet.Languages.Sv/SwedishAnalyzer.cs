using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Sv;

/// <summary>
/// Swedish (<c>"sv"</c>) language analyzer: Snowball Swedish stemmer plus a curated stop-word list.
/// Swedish has no standard phonetic encoder, so <see cref="Phonetic"/> is <see langword="null"/>.
/// </summary>
public sealed class SwedishAnalyzer : ILanguageAnalyzer
{
    /// <summary>The ISO 639-1 code for Swedish: <c>"sv"</c>.</summary>
    public string LanguageCode => "sv";

    /// <summary>The Snowball Swedish stemmer.</summary>
    public IStemmer Stemmer { get; } = new SnowballSwedishStemmer();

    /// <summary>Always <see langword="null"/>: no phonetic encoding is standard for Swedish.</summary>
    public IPhoneticEncoder? Phonetic => null;

    /// <summary>The Swedish stop-word filter.</summary>
    public IStopWordFilter? StopWords { get; } = new SwedishStopWords();
}
