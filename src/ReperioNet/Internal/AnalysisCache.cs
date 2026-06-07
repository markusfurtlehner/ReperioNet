using System.Collections.Concurrent;
using ReperioNet.Abstractions;

namespace ReperioNet.Internal;

/// <summary>
/// Per-batch memoization of stemmer and phonetic-encoder results. Email-like corpora have Zipfian
/// vocabularies, so within one bulk operation most tokens repeat; caching turns the per-token
/// Snowball/phonetic work into a dictionary hit. Bounded so a huge, diverse batch cannot grow the
/// cache without limit (entries past the cap are computed directly).
/// </summary>
internal sealed class AnalysisCache(IAnalyzerProvider analyzers)
{
    private const int CapacityPerCache = 100_000;

    private readonly ConcurrentDictionary<string, ILanguageAnalyzer> _byLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves the (cached) analyzer for a language, wrapping its stemmer/encoder with memoization.</summary>
    internal ILanguageAnalyzer Resolve(string? language)
        => _byLanguage.GetOrAdd(language ?? string.Empty, _ => new CachingAnalyzer(Analysis.Resolve(analyzers, language)));

    private sealed class CachingAnalyzer : ILanguageAnalyzer
    {
        internal CachingAnalyzer(ILanguageAnalyzer inner)
        {
            LanguageCode = inner.LanguageCode;
            Stemmer = new CachingStemmer(inner.Stemmer);
            Phonetic = inner.Phonetic is null ? null : new CachingEncoder(inner.Phonetic);
            StopWords = inner.StopWords;
        }

        public string LanguageCode { get; }

        public IStemmer Stemmer { get; }

        public IPhoneticEncoder? Phonetic { get; }

        public IStopWordFilter? StopWords { get; }
    }

    private sealed class CachingStemmer(IStemmer inner) : IStemmer
    {
        private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

        public string Stem(string token)
        {
            if (_cache.TryGetValue(token, out var stem))
            {
                return stem;
            }

            stem = inner.Stem(token);
            if (_cache.Count < CapacityPerCache)
            {
                _cache.TryAdd(token, stem);
            }

            return stem;
        }
    }

    private sealed class CachingEncoder(IPhoneticEncoder inner) : IPhoneticEncoder
    {
        private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.Ordinal);

        public string? Encode(string token)
        {
            if (_cache.TryGetValue(token, out var code))
            {
                return code;
            }

            code = inner.Encode(token);
            if (_cache.Count < CapacityPerCache)
            {
                _cache.TryAdd(token, code);
            }

            return code;
        }
    }
}
