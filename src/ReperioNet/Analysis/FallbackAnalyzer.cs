using ReperioNet.Abstractions;

namespace ReperioNet;

/// <summary>
/// Identity analyzer used for unknown/undetected languages: <see cref="IStemmer.Stem"/> returns the
/// token unchanged, no phonetic encoder, no stop words. Search still works on the base token stream.
/// </summary>
internal sealed class FallbackAnalyzer : ILanguageAnalyzer
{
    // Not a real language; the fallback is not tied to any ISO 639-1 code.
    public string LanguageCode => string.Empty;

    public IStemmer Stemmer { get; } = new IdentityStemmer();

    public IPhoneticEncoder? Phonetic => null;

    public IStopWordFilter? StopWords => null;

    private sealed class IdentityStemmer : IStemmer
    {
        public string Stem(string token) => token;
    }
}
