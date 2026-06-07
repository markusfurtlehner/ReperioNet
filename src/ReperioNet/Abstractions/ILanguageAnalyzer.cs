namespace ReperioNet.Abstractions;

/// <summary>Per-language analysis pipeline: stemming, optional phonetic encoding and optional stop words.</summary>
public interface ILanguageAnalyzer
{
    /// <summary>The ISO 639-1 code of the language this analyzer handles (e.g. <c>"de"</c>).</summary>
    string LanguageCode { get; }

    /// <summary>The stemmer for this language.</summary>
    IStemmer Stemmer { get; }

    /// <summary>The phonetic encoder for this language, or <see langword="null"/> if none is standard for it.</summary>
    IPhoneticEncoder? Phonetic { get; }

    /// <summary>The stop-word filter for this language, or <see langword="null"/> if none is provided.</summary>
    IStopWordFilter? StopWords { get; }
}
