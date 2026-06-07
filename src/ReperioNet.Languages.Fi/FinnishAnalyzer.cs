using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Fi;

/// <summary>
/// Finnish (<c>"fi"</c>) language analyzer: Snowball stemmer plus stop words. Finnish has no
/// standard phonetic encoding, so <see cref="Phonetic"/> is <see langword="null"/>.
/// </summary>
public sealed class FinnishAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "fi";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballFinnishStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic => null;

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new FinnishStopWords();
}
