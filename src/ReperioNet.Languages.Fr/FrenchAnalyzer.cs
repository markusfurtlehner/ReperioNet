using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Fr;

/// <summary>
/// French language analyzer (<c>"fr"</c>): Snowball French stemmer plus French stop words.
/// No phonetic encoder is provided because none is standard for French.
/// </summary>
public sealed class FrenchAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "fr";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballFrenchStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic => null;

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new FrenchStopWords();
}
