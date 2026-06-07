using ReperioNet.Abstractions;
using Xunit;

namespace ReperioNet.Tests;

public class AnalyzerProviderTests
{
    private sealed class StubAnalyzer(string languageCode) : ILanguageAnalyzer
    {
        public string LanguageCode => languageCode;

        public IStemmer Stemmer { get; } = new StubStemmer();

        public IPhoneticEncoder? Phonetic => null;

        public IStopWordFilter? StopWords => null;

        private sealed class StubStemmer : IStemmer
        {
            public string Stem(string token) => token;
        }
    }

    [Fact]
    public void Fallback_IsIdentityAnalyzer()
    {
        var provider = new DefaultAnalyzerProvider();
        var fallback = provider.Fallback;

        Assert.NotNull(fallback);
        Assert.Equal("rechnungen", fallback.Stemmer.Stem("rechnungen"));
        Assert.Equal("running", fallback.Stemmer.Stem("running"));
        Assert.Null(fallback.Phonetic);
        Assert.Null(fallback.StopWords);
    }

    [Fact]
    public void Get_UnknownLanguage_ReturnsNull()
    {
        var provider = new DefaultAnalyzerProvider();

        Assert.Null(provider.Get("de"));
        Assert.Null(provider.Get(""));
    }

    [Fact]
    public void Register_ThenGet_ReturnsAnalyzer()
    {
        var provider = new DefaultAnalyzerProvider();
        var analyzer = new StubAnalyzer("de");

        provider.Register(analyzer);

        Assert.Same(analyzer, provider.Get("de"));
    }

    [Fact]
    public void Register_LastRegistrationForACodeWins()
    {
        var provider = new DefaultAnalyzerProvider();
        var first = new StubAnalyzer("de");
        var second = new StubAnalyzer("de");

        provider.Register(first);
        provider.Register(second);

        Assert.Same(second, provider.Get("de"));
    }

    [Fact]
    public async Task Options_ExposeDefaultProviderWithFallback()
    {
        using var db = new TestDatabase();
        IAnalyzerProvider? seen = null;

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, o =>
        {
            o.MetadataTypeInfo = TestMetaJsonContext.Default.TestMeta;
            seen = o.Analyzers;
        }))
        {
        }

        Assert.NotNull(seen);
        Assert.IsType<DefaultAnalyzerProvider>(seen);
        Assert.NotNull(seen.Fallback);
    }
}
