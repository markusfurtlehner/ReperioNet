using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Pt;

/// <summary>
/// Portuguese language analyzer (<c>"pt"</c>): Snowball Portuguese stemmer plus Portuguese
/// stop words. No phonetic encoder is provided because none is standard for Portuguese.
/// </summary>
public sealed class PortugueseAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "pt";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballPortugueseStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic => null;

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new PortugueseStopWords();
}
