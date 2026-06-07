using ReperioNet.Languages.Tr;
using Xunit;

namespace ReperioNet.Languages.Tests;

public class TurkishPackTests
{
    private readonly SnowballTurkishStemmer _stemmer = new();

    [Theory]
    [InlineData("kitaplar", "kitap")]
    [InlineData("kitabımdır", "kitap")]
    [InlineData("kitabım", "kitap")]
    [InlineData("evler", "ev")]
    [InlineData("evdeki", "ev")]
    [InlineData("evden", "ev")]
    [InlineData("doktorsunuz", "doktor")]
    [InlineData("gözlerinde", "göz")]
    public void Stem_KnownVectors(string token, string expected)
        => Assert.Equal(expected, _stemmer.Stem(token));

    [Theory]
    [InlineData("kitaplar", "kitap")]
    [InlineData("kitaplar", "kitabım")]
    [InlineData("evler", "evdeki")]
    [InlineData("evden", "ev")]
    public void Stem_InflectionPairs_ShareAStem(string first, string second)
        => Assert.Equal(_stemmer.Stem(first), _stemmer.Stem(second));

    [Theory]
    [InlineData("kitaplar")]
    [InlineData("evler")]
    [InlineData("doktorsunuz")]
    public void Stem_IsIdempotentOnItsResult(string token)
    {
        var stem = _stemmer.Stem(token);

        Assert.Equal(stem, _stemmer.Stem(stem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("o")]
    [InlineData("ev")]
    public void Stem_EmptyAndShortTokens_AreSafe(string token)
        => Assert.NotNull(_stemmer.Stem(token));

    [Fact]
    public void Analyzer_ExposesExpectedPipeline()
    {
        var analyzer = new TurkishAnalyzer();

        Assert.Equal("tr", analyzer.LanguageCode);
        Assert.IsType<SnowballTurkishStemmer>(analyzer.Stemmer);
        Assert.Null(analyzer.Phonetic);
        Assert.NotNull(analyzer.StopWords);
    }

    [Theory]
    [InlineData("ve", true)]
    [InlineData("bir", true)]
    [InlineData("bu", true)]
    [InlineData("kitap", false)]
    public void StopWords_ClassifyTokens(string token, bool expected)
        => Assert.Equal(expected, new TurkishStopWords().IsStopWord(token));

    [Fact]
    public async Task Search_FindsEntryByAnotherInflectionOfTheSameLemma()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddTurkish();
        });

        await index.AddAsync(TestOptions.Entry("tr-1", "eski kitaplar rafta duruyor", "tr"));

        var hits = await index.SearchAsync("kitabım", new SearchQueryOptions { Language = "tr" });

        Assert.Contains(hits, hit => hit.Id == "tr-1");
    }
}
