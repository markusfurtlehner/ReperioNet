using ReperioNet.Languages.It;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>Italian language pack: stemmer vectors, analyzer wiring and end-to-end search.</summary>
public class ItalianPackTests
{
    private readonly SnowballItalianStemmer _stemmer = new();

    [Theory]
    // Verified against the official Snowball Italian test data (snowballstem.org).
    [InlineData("ragazzi", "ragazz")]
    [InlineData("ragazzo", "ragazz")]
    [InlineData("abbandonata", "abbandon")]
    [InlineData("abbandonati", "abbandon")]
    [InlineData("nazionale", "nazional")]
    [InlineData("nazionali", "nazional")]
    [InlineData("felicità", "felic")]
    [InlineData("velocemente", "veloc")]
    [InlineData("pericolosa", "pericol")]
    [InlineData("mangiando", "mang")]
    [InlineData("mangiare", "mang")]
    [InlineData("lettura", "lettur")]
    [InlineData("organizzazione", "organizz")]
    [InlineData("cantavano", "cant")]
    public void Stem_KnownVectors(string token, string expected)
        => Assert.Equal(expected, _stemmer.Stem(token));

    [Theory]
    [InlineData("ragazzi", "ragazzo")]
    [InlineData("abbandonata", "abbandonati")]
    [InlineData("nazionale", "nazionali")]
    [InlineData("mangiando", "mangiare")]
    [InlineData("cantavano", "cantare")]
    public void Stem_InflectionsOfSameLemma_ProduceSameStem(string first, string second)
        => Assert.Equal(_stemmer.Stem(first), _stemmer.Stem(second));

    [Theory]
    [InlineData("ragazz")]
    [InlineData("abbandon")]
    [InlineData("nazional")]
    [InlineData("felic")]
    [InlineData("organizz")]
    public void Stem_IsIdempotent(string token)
        => Assert.Equal(_stemmer.Stem(token), _stemmer.Stem(_stemmer.Stem(token)));

    [Fact]
    public void Stem_EmptyAndShortTokens_AreSafe()
    {
        Assert.Equal(string.Empty, _stemmer.Stem(string.Empty));
        Assert.Equal("a", _stemmer.Stem("a"));
        Assert.Equal("e", _stemmer.Stem("e"));
        Assert.Equal("di", _stemmer.Stem("di"));
    }

    [Fact]
    public void Analyzer_IsWiredCorrectly()
    {
        var analyzer = new ItalianAnalyzer();

        Assert.Equal("it", analyzer.LanguageCode);
        Assert.Null(analyzer.Phonetic);
        Assert.NotNull(analyzer.Stemmer);
        Assert.NotNull(analyzer.StopWords);
        Assert.True(analyzer.StopWords.IsStopWord("il"));
        Assert.True(analyzer.StopWords.IsStopWord("perché"));
        Assert.True(analyzer.StopWords.IsStopWord("della"));
        Assert.False(analyzer.StopWords.IsStopWord("ragazzo"));
    }

    [Fact]
    public async Task EndToEnd_StemmedSearchFindsOtherInflection()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddItalian();
        });

        await index.AddAsync(TestOptions.Entry("doc-it", "i ragazzi giocano in piazza", language: "it"));

        var hits = await index.SearchAsync("ragazzo", new SearchQueryOptions { Language = "it" });

        Assert.Contains(hits, h => h.Id == "doc-it");
    }
}
