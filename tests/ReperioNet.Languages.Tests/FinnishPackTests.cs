using ReperioNet.Languages.Fi;
using Xunit;

namespace ReperioNet.Languages.Tests;

public class FinnishPackTests
{
    private readonly SnowballFinnishStemmer _stemmer = new();

    [Theory]
    [InlineData("taloissa", "talo")]
    [InlineData("talot", "talo")]
    [InlineData("taloon", "talo")]
    [InlineData("taloissaan", "talo")]
    [InlineData("taloko", "talo")]
    [InlineData("talossa", "talo")]
    [InlineData("talosta", "talo")]
    [InlineData("kirjat", "kirj")]
    [InlineData("kirjan", "kirj")]
    [InlineData("edeltäjistään", "edeltäj")]
    [InlineData("edeltäjiin", "edeltäj")]
    public void Stem_KnownVectors(string token, string expected)
        => Assert.Equal(expected, _stemmer.Stem(token));

    [Theory]
    [InlineData("taloissa", "talot")]
    [InlineData("kirja", "kirjat")]
    [InlineData("taloon", "taloissaan")]
    public void Stem_InflectionPairs_ShareAStem(string first, string second)
        => Assert.Equal(_stemmer.Stem(first), _stemmer.Stem(second));

    [Theory]
    [InlineData("taloissa")]
    [InlineData("talot")]
    [InlineData("kirjat")]
    public void Stem_IsIdempotentOnItsResult(string token)
    {
        var stem = _stemmer.Stem(token);

        Assert.Equal(stem, _stemmer.Stem(stem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("on")]
    public void Stem_EmptyAndShortTokens_AreSafe(string token)
        => Assert.NotNull(_stemmer.Stem(token));

    [Fact]
    public void Analyzer_ExposesExpectedPipeline()
    {
        var analyzer = new FinnishAnalyzer();

        Assert.Equal("fi", analyzer.LanguageCode);
        Assert.IsType<SnowballFinnishStemmer>(analyzer.Stemmer);
        Assert.Null(analyzer.Phonetic);
        Assert.NotNull(analyzer.StopWords);
    }

    [Theory]
    [InlineData("ja", true)]
    [InlineData("on", true)]
    [InlineData("että", true)]
    [InlineData("talo", false)]
    public void StopWords_ClassifyTokens(string token, bool expected)
        => Assert.Equal(expected, new FinnishStopWords().IsStopWord(token));

    [Fact]
    public async Task Search_FindsEntryByAnotherInflectionOfTheSameLemma()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddFinnish();
        });

        await index.AddAsync(TestOptions.Entry("fi-1", "vanhat talot seisovat mäellä", "fi"));

        var hits = await index.SearchAsync("taloissa", new SearchQueryOptions { Language = "fi" });

        Assert.Contains(hits, hit => hit.Id == "fi-1");
    }
}
