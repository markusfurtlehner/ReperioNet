using ReperioNet.Languages.Nl;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>Dutch pack: Snowball stemmer vectors, analyzer wiring and end-to-end search.</summary>
public class DutchPackTests
{
    private static readonly SnowballDutchStemmer Stemmer = new();

    [Theory]
    [InlineData("kat", "kat")]
    [InlineData("katten", "kat")]
    [InlineData("boeken", "boek")]
    [InlineData("appelen", "appel")]
    [InlineData("werken", "werk")]
    [InlineData("gekken", "gek")]
    [InlineData("systemen", "system")]
    [InlineData("lichaam", "licham")]
    [InlineData("lichamen", "licham")]
    [InlineData("lichamelijk", "licham")]
    [InlineData("lichamelijke", "licham")]
    [InlineData("mogelijkheden", "mogelijk")]
    [InlineData("mogelijkheid", "mogelijk")]
    [InlineData("bedoeling", "bedoel")]
    [InlineData("grootste", "grootst")]
    [InlineData("maan", "man")]
    [InlineData("café", "caf")]
    public void Stem_ProducesSnowballOutput(string token, string expected)
    {
        Assert.Equal(expected, Stemmer.Stem(token));
    }

    [Theory]
    [InlineData("katten", "kat")]
    [InlineData("lichamen", "lichaam")]
    [InlineData("lichamelijke", "lichamelijk")]
    [InlineData("mogelijkheden", "mogelijkheid")]
    public void Stem_MapsInflectionsOfOneLemmaToSameStem(string first, string second)
    {
        Assert.Equal(Stemmer.Stem(second), Stemmer.Stem(first));
    }

    [Theory]
    [InlineData("katten")]
    [InlineData("boeken")]
    [InlineData("lichamelijke")]
    [InlineData("mogelijkheden")]
    [InlineData("bedoeling")]
    [InlineData("grootste")]
    [InlineData("maan")]
    [InlineData("systemen")]
    [InlineData("café")]
    public void Stem_IsIdempotent(string token)
    {
        var once = Stemmer.Stem(token);
        Assert.Equal(once, Stemmer.Stem(once));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ik")]
    [InlineData("de")]
    public void Stem_ShortTokens_AreSafeAndUnchanged(string token)
    {
        Assert.Equal(token, Stemmer.Stem(token));
    }

    [Fact]
    public void Analyzer_IsWiredCorrectly()
    {
        var analyzer = new DutchAnalyzer();

        Assert.Equal("nl", analyzer.LanguageCode);
        Assert.Null(analyzer.Phonetic);
        Assert.IsType<SnowballDutchStemmer>(analyzer.Stemmer);
        Assert.NotNull(analyzer.StopWords);

        Assert.True(analyzer.StopWords.IsStopWord("de"));
        Assert.True(analyzer.StopWords.IsStopWord("het"));
        Assert.True(analyzer.StopWords.IsStopWord("een"));
        Assert.False(analyzer.StopWords.IsStopWord("fiets"));
    }

    [Fact]
    public async Task EndToEnd_InflectedQueryFindsInflectedContent()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddDutch();
        });

        await index.AddAsync(TestOptions.Entry("nl-1", "De katten slapen in de tuin", language: "nl"));

        var hits = await index.SearchAsync("kat", new SearchQueryOptions { Language = "nl" });
        Assert.Contains(hits, hit => hit.Id == "nl-1");
    }
}
