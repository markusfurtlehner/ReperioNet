using ReperioNet.Abstractions;

namespace ReperioNet;

/// <summary>
/// Default <see cref="IAnalyzerProvider"/>: a dictionary of analyzers keyed by ISO 639-1 code
/// (case-insensitive) with an identity <see cref="Fallback"/> analyzer.
/// </summary>
internal sealed class DefaultAnalyzerProvider : IAnalyzerProvider
{
    private readonly Dictionary<string, ILanguageAnalyzer> _analyzers = new(StringComparer.OrdinalIgnoreCase);

    public ILanguageAnalyzer Fallback { get; } = new FallbackAnalyzer();

    public void Register(ILanguageAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);

        // Last registration for a code wins.
        _analyzers[analyzer.LanguageCode] = analyzer;
    }

    public ILanguageAnalyzer? Get(string languageCode)
    {
        if (string.IsNullOrEmpty(languageCode))
        {
            return null;
        }

        return _analyzers.TryGetValue(languageCode, out var analyzer) ? analyzer : null;
    }
}
