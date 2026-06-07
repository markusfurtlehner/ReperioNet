using ReperioNet.Languages.No;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>Norwegian pack: Snowball stemmer vectors, analyzer wiring and end-to-end search.</summary>
public class NorwegianPackTests
{
    private static readonly SnowballNorwegianStemmer Stemmer = new();

    [Theory]
    [InlineData("hus", "hus")]
    [InlineData("huset", "hus")]
    [InlineData("husene", "hus")]
    [InlineData("jenta", "jent")]
    [InlineData("jentene", "jent")]
    [InlineData("gutten", "gutt")]
    [InlineData("guttens", "gutt")]
    [InlineData("bøkene", "bøk")]
    [InlineData("serverte", "server")]
    [InlineData("serverer", "server")]
    [InlineData("sendt", "send")]
    [InlineData("vennlig", "venn")]
    [InlineData("hjertelig", "hjert")]
    [InlineData("verks", "verk")]
    [InlineData("fiskers", "fisk")]
    [InlineData("sommers", "sommers")]
    public void Stem_ProducesSnowballOutput(string token, string expected)
    {
        Assert.Equal(expected, Stemmer.Stem(token));
    }

    [Theory]
    [InlineData("huset", "husene")]
    [InlineData("jentene", "jenta")]
    [InlineData("guttens", "gutten")]
    [InlineData("serverte", "serverer")]
    public void Stem_MapsInflectionsOfOneLemmaToSameStem(string first, string second)
    {
        Assert.Equal(Stemmer.Stem(second), Stemmer.Stem(first));
    }

    [Theory]
    [InlineData("husene")]
    [InlineData("jentene")]
    [InlineData("guttens")]
    [InlineData("sendt")]
    [InlineData("vennlig")]
    [InlineData("hjertelig")]
    [InlineData("bøkene")]
    [InlineData("verks")]
    [InlineData("fiskers")]
    [InlineData("sommers")]
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
        var analyzer = new NorwegianAnalyzer();

        Assert.Equal("no", analyzer.LanguageCode);
        Assert.Null(analyzer.Phonetic);
        Assert.IsType<SnowballNorwegianStemmer>(analyzer.Stemmer);
        Assert.NotNull(analyzer.StopWords);

        Assert.True(analyzer.StopWords.IsStopWord("og"));
        Assert.True(analyzer.StopWords.IsStopWord("ikke"));
        Assert.True(analyzer.StopWords.IsStopWord("det"));
        Assert.False(analyzer.StopWords.IsStopWord("sykkel"));
    }

    [Fact]
    public async Task EndToEnd_InflectedQueryFindsInflectedContent()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddNorwegian();
        });

        await index.AddAsync(TestOptions.Entry("no-1", "guttene leker ute", language: "no"));

        var hits = await index.SearchAsync("gutten", new SearchQueryOptions { Language = "no" });
        Assert.Contains(hits, hit => hit.Id == "no-1");
    }
}
