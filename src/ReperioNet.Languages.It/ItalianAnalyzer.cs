using ReperioNet.Abstractions;

namespace ReperioNet.Languages.It;

/// <summary>
/// Italian language analyzer (<c>"it"</c>): Snowball Italian stemmer plus Italian stop words.
/// No phonetic encoder is provided because none is standard for Italian.
/// </summary>
public sealed class ItalianAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "it";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballItalianStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic => null;

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new ItalianStopWords();
}
