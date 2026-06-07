using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Ro;

/// <summary>
/// Romanian language analyzer (<c>"ro"</c>): Snowball Romanian stemmer plus Romanian stop
/// words. No phonetic encoder is provided because none is standard for Romanian.
/// </summary>
public sealed class RomanianAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "ro";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballRomanianStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic => null;

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new RomanianStopWords();
}
