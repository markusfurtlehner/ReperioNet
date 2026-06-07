using ReperioNet.Abstractions;

namespace ReperioNet.Tests;

/// <summary>Lambda-configurable analyzer stubs for exercising the core pipeline without language packs.</summary>
public sealed class StubAnalyzer(
    string languageCode,
    Func<string, string>? stem = null,
    Func<string, string?>? phonetic = null,
    params string[] stopWords) : ILanguageAnalyzer
{
    public string LanguageCode => languageCode;

    public IStemmer Stemmer { get; } = new StubStemmer(stem ?? (token => token));

    public IPhoneticEncoder? Phonetic { get; } = phonetic is null ? null : new StubPhonetic(phonetic);

    public IStopWordFilter? StopWords { get; } = stopWords.Length == 0 ? null : new StubStopWords(stopWords);

    private sealed class StubStemmer(Func<string, string> stem) : IStemmer
    {
        public string Stem(string token) => stem(token);
    }

    private sealed class StubPhonetic(Func<string, string?> encode) : IPhoneticEncoder
    {
        public string? Encode(string token) => encode(token);
    }

    private sealed class StubStopWords(string[] words) : IStopWordFilter
    {
        private readonly HashSet<string> _words = new(words, StringComparer.Ordinal);

        public bool IsStopWord(string token) => _words.Contains(token);
    }
}

public static class TestAnalyzerFactory
{
    /// <summary>Toy German-ish analyzer: strips a trailing "en" from tokens.</summary>
    public static StubAnalyzer GermanSuffixStripper(params string[] stopWords)
        => new("de", token => token.EndsWith("en", StringComparison.Ordinal) ? token[..^2] : token, stopWords: stopWords);

    /// <summary>Toy phonetic analyzer: identity stemmer plus a vowel-stripping encoder.</summary>
    public static StubAnalyzer VowelStripPhonetic(string code = "de")
        => new(code, phonetic: token =>
        {
            var encoded = new string(token.Where(c => !"aeiouäöü".Contains(c)).ToArray());
            return encoded.Length == 0 ? null : encoded;
        });
}
