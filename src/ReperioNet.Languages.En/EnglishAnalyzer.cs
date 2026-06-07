using ReperioNet.Abstractions;

namespace ReperioNet.Languages.En;

/// <summary>
/// English ("en") analysis pipeline: Snowball (Porter2) stemming, Double Metaphone phonetic
/// encoding and English stop words. Stateless and safe for concurrent use.
/// </summary>
public sealed class EnglishAnalyzer : ILanguageAnalyzer
{
    /// <summary>The ISO 639-1 code for English.</summary>
    public string LanguageCode => "en";

    /// <summary>The Snowball "english" (Porter2) stemmer.</summary>
    public IStemmer Stemmer { get; } = new SnowballEnglishStemmer();

    /// <summary>The Double Metaphone phonetic encoder.</summary>
    public IPhoneticEncoder? Phonetic { get; } = new DoubleMetaphone();

    /// <summary>The curated English stop-word list.</summary>
    public IStopWordFilter? StopWords { get; } = new EnglishStopWords();
}
