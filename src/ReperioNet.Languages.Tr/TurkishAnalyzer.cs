using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Tr;

/// <summary>
/// Turkish (<c>"tr"</c>) language analyzer: Snowball stemmer plus stop words. Turkish has no
/// standard phonetic encoding, so <see cref="Phonetic"/> is <see langword="null"/>.
/// </summary>
public sealed class TurkishAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "tr";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballTurkishStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic => null;

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new TurkishStopWords();
}
