using ReperioNet.Languages.Da;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>Danish pack: Snowball stemmer vectors, analyzer wiring and end-to-end search.</summary>
public class DanishPackTests
{
    private static readonly SnowballDanishStemmer Stemmer = new();

    [Theory]
    [InlineData("hund", "hund")]
    [InlineData("hunden", "hund")]
    [InlineData("hundene", "hund")]
    [InlineData("hunds", "hund")]
    [InlineData("kvinde", "kvind")]
    [InlineData("kvinden", "kvind")]
    [InlineData("bogen", "bog")]
    [InlineData("venlig", "ven")]
    [InlineData("venligst", "ven")]
    [InlineData("frygteligt", "frygt")]
    [InlineData("hoppen", "hop")]
    [InlineData("friheden", "frihed")]
    [InlineData("frihedens", "frihed")]
    [InlineData("blandt", "bland")]
    [InlineData("modløst", "modløs")]
    public void Stem_ProducesSnowballOutput(string token, string expected)
    {
        Assert.Equal(expected, Stemmer.Stem(token));
    }

    [Theory]
    [InlineData("hunden", "hund")]
    [InlineData("hundene", "hunden")]
    [InlineData("kvinden", "kvinde")]
    [InlineData("venligst", "venlig")]
    [InlineData("frihedens", "friheden")]
    public void Stem_MapsInflectionsOfOneLemmaToSameStem(string first, string second)
    {
        Assert.Equal(Stemmer.Stem(second), Stemmer.Stem(first));
    }

    [Theory]
    [InlineData("hundene")]
    [InlineData("hunds")]
    [InlineData("kvinden")]
    [InlineData("venligst")]
    [InlineData("frygteligt")]
    [InlineData("hoppen")]
    [InlineData("frihedens")]
    [InlineData("blandt")]
    [InlineData("modløst")]
    public void Stem_IsIdempotent(string token)
    {
        var once = Stemmer.Stem(token);
        Assert.Equal(once, Stemmer.Stem(once));
    }

    [Theory]
    [InlineData("")]
    [InlineData("å")]
    [InlineData("og")]
    [InlineData("på")]
    public void Stem_ShortTokens_AreSafeAndUnchanged(string token)
    {
        Assert.Equal(token, Stemmer.Stem(token));
    }

    [Fact]
    public void Analyzer_IsWiredCorrectly()
    {
        var analyzer = new DanishAnalyzer();

        Assert.Equal("da", analyzer.LanguageCode);
        Assert.Null(analyzer.Phonetic);
        Assert.IsType<SnowballDanishStemmer>(analyzer.Stemmer);
        Assert.NotNull(analyzer.StopWords);

        Assert.True(analyzer.StopWords.IsStopWord("og"));
        Assert.True(analyzer.StopWords.IsStopWord("ikke"));
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
            o.AddDanish();
        });

        await index.AddAsync(TestOptions.Entry("da-1", "hundene løber hurtigt", language: "da"));

        var hits = await index.SearchAsync("hunden", new SearchQueryOptions { Language = "da" });
        Assert.Contains(hits, hit => hit.Id == "da-1");
    }
}
