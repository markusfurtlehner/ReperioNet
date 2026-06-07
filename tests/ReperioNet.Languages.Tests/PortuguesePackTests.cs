using ReperioNet.Languages.Pt;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>Portuguese language pack: stemmer vectors, analyzer wiring and end-to-end search.</summary>
public class PortuguesePackTests
{
    private readonly SnowballPortugueseStemmer _stemmer = new();

    [Theory]
    // Verified against the official Snowball Portuguese test data (snowballstem.org).
    [InlineData("casas", "cas")]
    [InlineData("casa", "cas")]
    [InlineData("meninas", "menin")]
    [InlineData("menino", "menin")]
    [InlineData("nacionalidade", "nacional")]
    [InlineData("rapidamente", "rapid")]
    [InlineData("importância", "import")]
    [InlineData("trabalhando", "trabalh")]
    [InlineData("trabalhava", "trabalh")]
    [InlineData("cantaram", "cant")]
    [InlineData("cantar", "cant")]
    [InlineData("felicidade", "felic")]
    [InlineData("organização", "organiz")]
    [InlineData("coração", "coraçã")]
    public void Stem_KnownVectors(string token, string expected)
        => Assert.Equal(expected, _stemmer.Stem(token));

    [Theory]
    [InlineData("casas", "casa")]
    [InlineData("meninas", "menino")]
    [InlineData("trabalhando", "trabalhava")]
    [InlineData("cantaram", "cantar")]
    [InlineData("falaram", "falavam")]
    public void Stem_InflectionsOfSameLemma_ProduceSameStem(string first, string second)
        => Assert.Equal(_stemmer.Stem(first), _stemmer.Stem(second));

    [Theory]
    [InlineData("cas")]
    [InlineData("menin")]
    [InlineData("nacional")]
    [InlineData("trabalh")]
    [InlineData("coraçã")]
    public void Stem_IsIdempotent(string token)
        => Assert.Equal(_stemmer.Stem(token), _stemmer.Stem(_stemmer.Stem(token)));

    [Fact]
    public void Stem_EmptyAndShortTokens_AreSafe()
    {
        Assert.Equal(string.Empty, _stemmer.Stem(string.Empty));
        Assert.Equal("a", _stemmer.Stem("a"));
        Assert.Equal("e", _stemmer.Stem("e"));
        Assert.Equal("de", _stemmer.Stem("de"));
    }

    [Fact]
    public void Analyzer_IsWiredCorrectly()
    {
        var analyzer = new PortugueseAnalyzer();

        Assert.Equal("pt", analyzer.LanguageCode);
        Assert.Null(analyzer.Phonetic);
        Assert.NotNull(analyzer.Stemmer);
        Assert.NotNull(analyzer.StopWords);
        Assert.True(analyzer.StopWords.IsStopWord("os"));
        Assert.True(analyzer.StopWords.IsStopWord("não"));
        Assert.True(analyzer.StopWords.IsStopWord("também"));
        Assert.False(analyzer.StopWords.IsStopWord("menino"));
    }

    [Fact]
    public async Task EndToEnd_StemmedSearchFindsOtherInflection()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddPortuguese();
        });

        await index.AddAsync(TestOptions.Entry("doc-pt", "as casas ficam perto do rio", language: "pt"));

        var hits = await index.SearchAsync("casa", new SearchQueryOptions { Language = "pt" });

        Assert.Contains(hits, h => h.Id == "doc-pt");
    }
}
