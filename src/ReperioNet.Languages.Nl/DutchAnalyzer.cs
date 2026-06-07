using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Nl;

/// <summary>
/// Dutch (<c>"nl"</c>) language analyzer: Snowball Dutch stemmer plus a curated stop-word list.
/// Dutch has no standard phonetic encoder, so <see cref="Phonetic"/> is <see langword="null"/>.
/// </summary>
public sealed class DutchAnalyzer : ILanguageAnalyzer
{
    /// <summary>The ISO 639-1 code for Dutch: <c>"nl"</c>.</summary>
    public string LanguageCode => "nl";

    /// <summary>The Snowball Dutch stemmer.</summary>
    public IStemmer Stemmer { get; } = new SnowballDutchStemmer();

    /// <summary>Always <see langword="null"/>: no phonetic encoding is standard for Dutch.</summary>
    public IPhoneticEncoder? Phonetic => null;

    /// <summary>The Dutch stop-word filter.</summary>
    public IStopWordFilter? StopWords { get; } = new DutchStopWords();
}
