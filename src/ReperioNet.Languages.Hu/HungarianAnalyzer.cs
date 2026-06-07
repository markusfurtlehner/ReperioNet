using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Hu;

/// <summary>
/// Hungarian (<c>"hu"</c>) language analyzer: Snowball stemmer plus stop words. Hungarian has no
/// standard phonetic encoding, so <see cref="Phonetic"/> is <see langword="null"/>.
/// </summary>
public sealed class HungarianAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "hu";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballHungarianStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic => null;

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new HungarianStopWords();
}
