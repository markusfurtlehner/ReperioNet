using ReperioNet.Languages.Hu;
using Xunit;

namespace ReperioNet.Languages.Tests;

public class HungarianPackTests
{
    private readonly SnowballHungarianStemmer _stemmer = new();

    [Theory]
    [InlineData("házak", "ház")]
    [InlineData("házat", "ház")]
    [InlineData("háznak", "ház")]
    [InlineData("házzal", "ház")]
    [InlineData("házban", "ház")]
    [InlineData("házaim", "ház")]
    [InlineData("autók", "autó")]
    [InlineData("babakocsival", "babakocs")]
    [InlineData("almában", "alm")]
    public void Stem_KnownVectors(string token, string expected)
        => Assert.Equal(expected, _stemmer.Stem(token));

    [Theory]
    [InlineData("házak", "ház")]
    [InlineData("házat", "háznak")]
    [InlineData("almában", "alma")]
    [InlineData("autók", "autó")]
    public void Stem_InflectionPairs_ShareAStem(string first, string second)
        => Assert.Equal(_stemmer.Stem(first), _stemmer.Stem(second));

    [Theory]
    [InlineData("házak")]
    [InlineData("autók")]
    [InlineData("babakocsival")]
    public void Stem_IsIdempotentOnItsResult(string token)
    {
        var stem = _stemmer.Stem(token);

        Assert.Equal(stem, _stemmer.Stem(stem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("az")]
    public void Stem_EmptyAndShortTokens_AreSafe(string token)
        => Assert.NotNull(_stemmer.Stem(token));

    [Fact]
    public void Analyzer_ExposesExpectedPipeline()
    {
        var analyzer = new HungarianAnalyzer();

        Assert.Equal("hu", analyzer.LanguageCode);
        Assert.IsType<SnowballHungarianStemmer>(analyzer.Stemmer);
        Assert.Null(analyzer.Phonetic);
        Assert.NotNull(analyzer.StopWords);
    }

    [Theory]
    [InlineData("és", true)]
    [InlineData("a", true)]
    [InlineData("az", true)]
    [InlineData("ház", false)]
    public void StopWords_ClassifyTokens(string token, bool expected)
        => Assert.Equal(expected, new HungarianStopWords().IsStopWord(token));

    [Fact]
    public async Task Search_FindsEntryByAnotherInflectionOfTheSameLemma()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddHungarian();
        });

        await index.AddAsync(TestOptions.Entry("hu-1", "a házak a domb tetején állnak", "hu"));

        var hits = await index.SearchAsync("házat", new SearchQueryOptions { Language = "hu" });

        Assert.Contains(hits, hit => hit.Id == "hu-1");
    }
}
