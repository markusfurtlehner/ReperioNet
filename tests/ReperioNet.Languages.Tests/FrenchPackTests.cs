using ReperioNet.Languages.Fr;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>French language pack: stemmer vectors, analyzer wiring and end-to-end search.</summary>
public class FrenchPackTests
{
    private readonly SnowballFrenchStemmer _stemmer = new();

    [Theory]
    // Verified against the official Snowball French test data (snowballstem.org).
    [InlineData("chevaux", "cheval")]
    [InlineData("cheval", "cheval")]
    [InlineData("continuité", "continu")]
    [InlineData("continuer", "continu")]
    [InlineData("majestueux", "majestu")]
    [InlineData("majestueuse", "majestu")]
    [InlineData("précieusement", "précieux")]
    [InlineData("mangeait", "mang")]
    [InlineData("nationalité", "national")]
    [InlineData("principalement", "principal")]
    [InlineData("jouaient", "jou")]
    [InlineData("finissions", "fin")]
    [InlineData("yeux", "yeux")]
    [InlineData("nation", "nation")]
    public void Stem_KnownVectors(string token, string expected)
        => Assert.Equal(expected, _stemmer.Stem(token));

    [Theory]
    [InlineData("chevaux", "cheval")]
    [InlineData("mangeait", "mangeaient")]
    [InlineData("continuer", "continua")]
    [InlineData("majestueux", "majestueuse")]
    [InlineData("finissions", "finissaient")]
    public void Stem_InflectionsOfSameLemma_ProduceSameStem(string first, string second)
        => Assert.Equal(_stemmer.Stem(first), _stemmer.Stem(second));

    [Theory]
    [InlineData("cheval")]
    [InlineData("continu")]
    [InlineData("précieux")]
    [InlineData("national")]
    [InlineData("jou")]
    public void Stem_IsIdempotent(string token)
        => Assert.Equal(_stemmer.Stem(token), _stemmer.Stem(_stemmer.Stem(token)));

    [Fact]
    public void Stem_EmptyAndShortTokens_AreSafe()
    {
        Assert.Equal(string.Empty, _stemmer.Stem(string.Empty));
        Assert.Equal("a", _stemmer.Stem("a"));
        Assert.Equal("de", _stemmer.Stem("de"));
        Assert.Equal("le", _stemmer.Stem("le"));
    }

    [Fact]
    public void Analyzer_IsWiredCorrectly()
    {
        var analyzer = new FrenchAnalyzer();

        Assert.Equal("fr", analyzer.LanguageCode);
        Assert.Null(analyzer.Phonetic);
        Assert.NotNull(analyzer.Stemmer);
        Assert.NotNull(analyzer.StopWords);
        Assert.True(analyzer.StopWords.IsStopWord("le"));
        Assert.True(analyzer.StopWords.IsStopWord("être"));
        Assert.True(analyzer.StopWords.IsStopWord("avec"));
        Assert.False(analyzer.StopWords.IsStopWord("cheval"));
    }

    [Fact]
    public async Task EndToEnd_StemmedSearchFindsOtherInflection()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddFrench();
        });

        await index.AddAsync(TestOptions.Entry("doc-fr", "les chevaux galopent dans le pré", language: "fr"));

        var hits = await index.SearchAsync("cheval", new SearchQueryOptions { Language = "fr" });

        Assert.Contains(hits, h => h.Id == "doc-fr");
    }
}
