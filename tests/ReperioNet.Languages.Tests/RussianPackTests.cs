using ReperioNet.Languages.Ru;
using Xunit;

namespace ReperioNet.Languages.Tests;

public class RussianPackTests
{
    private readonly SnowballRussianStemmer _stemmer = new();

    [Theory]
    [InlineData("книги", "книг")]
    [InlineData("книга", "книг")]
    [InlineData("столы", "стол")]
    [InlineData("читала", "чита")]
    [InlineData("читать", "чита")]
    [InlineData("жизнь", "жизн")]
    [InlineData("жизни", "жизн")]
    [InlineData("красивый", "красив")]
    [InlineData("важность", "важност")]
    [InlineData("техники", "техник")]
    [InlineData("ёлка", "елк")]
    public void Stem_KnownVectors(string token, string expected)
        => Assert.Equal(expected, _stemmer.Stem(token));

    [Theory]
    [InlineData("книги", "книга")]
    [InlineData("красивый", "красивая")]
    [InlineData("важность", "важности")]
    [InlineData("читала", "читать")]
    public void Stem_InflectionPairs_ShareAStem(string first, string second)
        => Assert.Equal(_stemmer.Stem(first), _stemmer.Stem(second));

    [Theory]
    [InlineData("книги")]
    [InlineData("столы")]
    [InlineData("техника")]
    public void Stem_IsIdempotentOnItsResult(string token)
    {
        var stem = _stemmer.Stem(token);

        Assert.Equal(stem, _stemmer.Stem(stem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("и")]
    [InlineData("он")]
    public void Stem_EmptyAndShortTokens_AreSafe(string token)
        => Assert.NotNull(_stemmer.Stem(token));

    [Fact]
    public void Analyzer_ExposesExpectedPipeline()
    {
        var analyzer = new RussianAnalyzer();

        Assert.Equal("ru", analyzer.LanguageCode);
        Assert.IsType<SnowballRussianStemmer>(analyzer.Stemmer);
        Assert.Null(analyzer.Phonetic);
        Assert.NotNull(analyzer.StopWords);
    }

    [Theory]
    [InlineData("и", true)]
    [InlineData("в", true)]
    [InlineData("не", true)]
    [InlineData("книга", false)]
    public void StopWords_ClassifyTokens(string token, bool expected)
        => Assert.Equal(expected, new RussianStopWords().IsStopWord(token));

    [Fact]
    public async Task Search_FindsEntryByAnotherInflectionOfTheSameLemma()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddRussian();
        });

        await index.AddAsync(TestOptions.Entry("ru-1", "старые книги лежат на полке", "ru"));

        var hits = await index.SearchAsync("книга", new SearchQueryOptions { Language = "ru" });

        Assert.Contains(hits, hit => hit.Id == "ru-1");
    }
}
