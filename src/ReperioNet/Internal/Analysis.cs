using ReperioNet.Abstractions;

namespace ReperioNet.Internal;

/// <summary>
/// Analyzer resolution and stem/phonetic stream derivation (PRD §6.4–6.5, §9.2–9.3). The same code
/// runs at index time, during rebuild and on the query side so the streams always agree.
/// </summary>
internal static class Analysis
{
    /// <summary>Picks the analyzer for a resolved language code, falling back to the identity analyzer (§6.4).</summary>
    internal static ILanguageAnalyzer Resolve(IAnalyzerProvider analyzers, string? languageCode)
        => (string.IsNullOrEmpty(languageCode) ? null : analyzers.Get(languageCode)) ?? analyzers.Fallback;

    /// <summary>
    /// Stems each token (deduped, first-seen order preserved). When <paramref name="removeStopWords"/>
    /// is set and the analyzer provides a filter, stop words are dropped before stemming (§6.5).
    /// </summary>
    internal static List<string> StemTokens(ILanguageAnalyzer analyzer, IReadOnlyList<string> tokens, bool removeStopWords)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stopWords = removeStopWords ? analyzer.StopWords : null;
        foreach (var token in tokens)
        {
            if (stopWords?.IsStopWord(token) == true)
            {
                continue;
            }

            var stem = analyzer.Stemmer.Stem(token);
            if (!string.IsNullOrEmpty(stem) && seen.Add(stem))
            {
                result.Add(stem);
            }
        }

        return result;
    }

    /// <summary>
    /// Phonetically encodes each token (null/empty codes skipped, deduped, first-seen order). Returns
    /// an empty list when the analyzer has no encoder. Stop-word removal applies as for stems (§6.5).
    /// </summary>
    internal static List<string> PhoneticTokens(ILanguageAnalyzer analyzer, IReadOnlyList<string> tokens, bool removeStopWords)
    {
        var result = new List<string>();
        if (analyzer.Phonetic is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stopWords = removeStopWords ? analyzer.StopWords : null;
        foreach (var token in tokens)
        {
            if (stopWords?.IsStopWord(token) == true)
            {
                continue;
            }

            var code = analyzer.Phonetic.Encode(token);
            if (!string.IsNullOrEmpty(code) && seen.Add(code))
            {
                result.Add(code);
            }
        }

        return result;
    }
}
