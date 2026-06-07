using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Ru;

/// <summary>
/// Russian (<c>"ru"</c>) language analyzer: Snowball stemmer plus stop words. Russian has no
/// standard phonetic encoding, so <see cref="Phonetic"/> is <see langword="null"/>.
/// </summary>
public sealed class RussianAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "ru";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballRussianStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic => null;

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new RussianStopWords();
}
