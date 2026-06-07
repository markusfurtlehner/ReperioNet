using ReperioNet.Languages.Sv;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>Swedish pack: Snowball stemmer vectors, analyzer wiring and end-to-end search.</summary>
public class SwedishPackTests
{
    private static readonly SnowballSwedishStemmer Stemmer = new();

    [Theory]
    [InlineData("hus", "hus")]
    [InlineData("huset", "hus")]
    [InlineData("husets", "hus")]
    [InlineData("flicka", "flick")]
    [InlineData("flickor", "flick")]
    [InlineData("flickorna", "flick")]
    [InlineData("jakten", "jakt")]
    [InlineData("starkare", "stark")]
    [InlineData("starkast", "stark")]
    [InlineData("möjligheterna", "möj")]
    [InlineData("friheten", "frihet")]
    [InlineData("tryggt", "trygg")]
    [InlineData("kraftfullt", "kraftfull")]
    [InlineData("rastlöst", "rastlös")]
    [InlineData("paket", "paket")]
    [InlineData("nyhet", "nyhet")]
    public void Stem_ProducesSnowballOutput(string token, string expected)
    {
        Assert.Equal(expected, Stemmer.Stem(token));
    }

    [Theory]
    [InlineData("flickorna", "flicka")]
    [InlineData("husets", "huset")]
    [InlineData("starkare", "starkast")]
    [InlineData("tryggt", "trygg")]
    public void Stem_MapsInflectionsOfOneLemmaToSameStem(string first, string second)
    {
        Assert.Equal(Stemmer.Stem(second), Stemmer.Stem(first));
    }

    [Theory]
    [InlineData("flickorna")]
    [InlineData("husets")]
    [InlineData("jakten")]
    [InlineData("starkare")]
    [InlineData("möjligheterna")]
    [InlineData("friheten")]
    [InlineData("tryggt")]
    [InlineData("kraftfullt")]
    [InlineData("rastlöst")]
    public void Stem_IsIdempotent(string token)
    {
        var once = Stemmer.Stem(token);
        Assert.Equal(once, Stemmer.Stem(once));
    }

    [Theory]
    [InlineData("")]
    [InlineData("å")]
    [InlineData("en")]
    [InlineData("och")]
    public void Stem_ShortTokens_AreSafeAndUnchanged(string token)
    {
        Assert.Equal(token, Stemmer.Stem(token));
    }

    [Fact]
    public void Analyzer_IsWiredCorrectly()
    {
        var analyzer = new SwedishAnalyzer();

        Assert.Equal("sv", analyzer.LanguageCode);
        Assert.Null(analyzer.Phonetic);
        Assert.IsType<SnowballSwedishStemmer>(analyzer.Stemmer);
        Assert.NotNull(analyzer.StopWords);

        Assert.True(analyzer.StopWords.IsStopWord("och"));
        Assert.True(analyzer.StopWords.IsStopWord("att"));
        Assert.True(analyzer.StopWords.IsStopWord("det"));
        Assert.False(analyzer.StopWords.IsStopWord("cykel"));
    }

    [Fact]
    public async Task EndToEnd_InflectedQueryFindsInflectedContent()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddSwedish();
        });

        await index.AddAsync(TestOptions.Entry("sv-1", "flickorna leker i parken", language: "sv"));

        var hits = await index.SearchAsync("flicka", new SearchQueryOptions { Language = "sv" });
        Assert.Contains(hits, hit => hit.Id == "sv-1");
    }
}
