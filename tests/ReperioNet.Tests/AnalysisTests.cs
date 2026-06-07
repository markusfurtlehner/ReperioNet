using ReperioNet.Internal;
using Xunit;

namespace ReperioNet.Tests;

public class AnalysisTests
{
    [Fact]
    public void Resolve_UnknownOrNullLanguage_ReturnsFallback()
    {
        var provider = new DefaultAnalyzerProvider();

        Assert.Same(provider.Fallback, Analysis.Resolve(provider, null));
        Assert.Same(provider.Fallback, Analysis.Resolve(provider, ""));
        Assert.Same(provider.Fallback, Analysis.Resolve(provider, "xx"));
    }

    [Fact]
    public void Resolve_RegisteredLanguage_ReturnsItsAnalyzer()
    {
        var provider = new DefaultAnalyzerProvider();
        var analyzer = new StubAnalyzer("de");
        provider.Register(analyzer);

        Assert.Same(analyzer, Analysis.Resolve(provider, "de"));
    }

    [Fact]
    public void StemTokens_DedupesKeepingFirstSeenOrder()
    {
        var analyzer = new StubAnalyzer("xx");

        var stems = Analysis.StemTokens(analyzer, ["beta", "alpha", "beta"], removeStopWords: false);

        Assert.Equal(["beta", "alpha"], stems);
    }

    [Fact]
    public void StemTokens_DedupesByStemmedForm()
    {
        var analyzer = TestAnalyzerFactory.GermanSuffixStripper();

        // "rechnungen" and "rechnung" collapse to the same stem.
        var stems = Analysis.StemTokens(analyzer, ["rechnungen", "rechnung"], removeStopWords: false);

        Assert.Equal(["rechnung"], stems);
    }

    [Fact]
    public void StemTokens_SkipsEmptyStems()
    {
        var analyzer = new StubAnalyzer("xx", stem: t => t == "drop" ? "" : t);

        var stems = Analysis.StemTokens(analyzer, ["drop", "keep"], removeStopWords: false);

        Assert.Equal(["keep"], stems);
    }

    [Fact]
    public void StemTokens_RemovesStopWordsBeforeStemming()
    {
        var analyzer = new StubAnalyzer("xx", stopWords: "und");

        var stems = Analysis.StemTokens(analyzer, ["und", "kafka"], removeStopWords: true);

        Assert.Equal(["kafka"], stems);
    }

    [Fact]
    public void StemTokens_StopWordsIgnoredWhenFlagOff()
    {
        var analyzer = new StubAnalyzer("xx", stopWords: "und");

        var stems = Analysis.StemTokens(analyzer, ["und", "kafka"], removeStopWords: false);

        Assert.Equal(["und", "kafka"], stems);
    }

    [Fact]
    public void PhoneticTokens_NoEncoder_ReturnsEmpty()
    {
        var analyzer = new StubAnalyzer("xx");

        Assert.Empty(Analysis.PhoneticTokens(analyzer, ["kafka"], removeStopWords: false));
    }

    [Fact]
    public void PhoneticTokens_SkipsNullCodes_AndDedupes()
    {
        var analyzer = new StubAnalyzer("xx", phonetic: t => t == "skip" ? null : "code");

        var codes = Analysis.PhoneticTokens(analyzer, ["skip", "one", "two"], removeStopWords: false);

        Assert.Equal(["code"], codes);
    }
}
