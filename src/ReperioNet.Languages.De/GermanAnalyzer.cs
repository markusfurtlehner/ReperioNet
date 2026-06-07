using ReperioNet.Abstractions;

namespace ReperioNet.Languages.De;

/// <summary>
/// German ("de") analysis pipeline: <see cref="SnowballGermanStemmer"/> for stemming,
/// <see cref="KoelnerPhonetik"/> for phonetic encoding and <see cref="GermanStopWords"/> as the
/// stop-word filter.
/// </summary>
public sealed class GermanAnalyzer : ILanguageAnalyzer
{
    /// <inheritdoc />
    public string LanguageCode => "de";

    /// <inheritdoc />
    public IStemmer Stemmer { get; } = new SnowballGermanStemmer();

    /// <inheritdoc />
    public IPhoneticEncoder? Phonetic { get; } = new KoelnerPhonetik();

    /// <inheritdoc />
    public IStopWordFilter? StopWords { get; } = new GermanStopWords();
}
