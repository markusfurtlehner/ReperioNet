namespace ReperioNet.Abstractions;

/// <summary>Registry of <see cref="ILanguageAnalyzer"/> instances keyed by ISO 639-1 language code.</summary>
public interface IAnalyzerProvider
{
    /// <summary>Registers <paramref name="analyzer"/> under its <see cref="ILanguageAnalyzer.LanguageCode"/>. The last registration for a code wins.</summary>
    /// <param name="analyzer">The analyzer to register.</param>
    void Register(ILanguageAnalyzer analyzer);

    /// <summary>Returns the analyzer registered for <paramref name="languageCode"/>, or <see langword="null"/> if none is registered.</summary>
    /// <param name="languageCode">An ISO 639-1 language code.</param>
    ILanguageAnalyzer? Get(string languageCode);

    /// <summary>The identity fallback analyzer (base tokens only) used for unknown or undetected languages.</summary>
    ILanguageAnalyzer Fallback { get; }
}
