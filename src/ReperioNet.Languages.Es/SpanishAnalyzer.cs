using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Es;

/// <summary>
/// Spanish language analyzer (<c>"es"</c>): Snowball Spanish stemmer plus Spanish stop words.
/// No phonetic encoder is provided because none is standard for Spanish.
/// </summary>
public sealed class SpanishAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "es";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballSpanishStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic => null;

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new SpanishStopWords();
}
