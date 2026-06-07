using ReperioNet.Languages.Es;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>Spanish language pack: stemmer vectors, analyzer wiring and end-to-end search.</summary>
public class SpanishPackTests
{
    private readonly SnowballSpanishStemmer _stemmer = new();

    [Theory]
    // Verified against the official Snowball Spanish test data (snowballstem.org).
    [InlineData("gatos", "gat")]
    [InlineData("gato", "gat")]
    [InlineData("niñas", "niñ")]
    [InlineData("niño", "niñ")]
    [InlineData("canciones", "cancion")]
    [InlineData("canción", "cancion")]
    [InlineData("importancia", "import")]
    [InlineData("nacionalidad", "nacional")]
    [InlineData("rápidamente", "rapid")]
    [InlineData("lógica", "logic")]
    [InlineData("trabajando", "trabaj")]
    [InlineData("llegué", "lleg")]
    [InlineData("comieron", "com")]
    [InlineData("comer", "com")]
    public void Stem_KnownVectors(string token, string expected)
        => Assert.Equal(expected, _stemmer.Stem(token));

    [Theory]
    [InlineData("gatos", "gato")]
    [InlineData("canciones", "canción")]
    [InlineData("trabajando", "trabajaba")]
    [InlineData("comieron", "comer")]
    [InlineData("importancia", "importante")]
    public void Stem_InflectionsOfSameLemma_ProduceSameStem(string first, string second)
        => Assert.Equal(_stemmer.Stem(first), _stemmer.Stem(second));

    [Theory]
    [InlineData("gat")]
    [InlineData("cancion")]
    [InlineData("nacional")]
    [InlineData("trabaj")]
    [InlineData("logic")]
    public void Stem_IsIdempotent(string token)
        => Assert.Equal(_stemmer.Stem(token), _stemmer.Stem(_stemmer.Stem(token)));

    [Fact]
    public void Stem_EmptyAndShortTokens_AreSafe()
    {
        Assert.Equal(string.Empty, _stemmer.Stem(string.Empty));
        Assert.Equal("a", _stemmer.Stem("a"));
        Assert.Equal("y", _stemmer.Stem("y"));
        Assert.Equal("de", _stemmer.Stem("de"));
    }

    [Fact]
    public void Analyzer_IsWiredCorrectly()
    {
        var analyzer = new SpanishAnalyzer();

        Assert.Equal("es", analyzer.LanguageCode);
        Assert.Null(analyzer.Phonetic);
        Assert.NotNull(analyzer.Stemmer);
        Assert.NotNull(analyzer.StopWords);
        Assert.True(analyzer.StopWords.IsStopWord("el"));
        Assert.True(analyzer.StopWords.IsStopWord("según"));
        Assert.True(analyzer.StopWords.IsStopWord("nosotros"));
        Assert.False(analyzer.StopWords.IsStopWord("gato"));
    }

    [Fact]
    public async Task EndToEnd_StemmedSearchFindsOtherInflection()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddSpanish();
        });

        await index.AddAsync(TestOptions.Entry("doc-es", "los gatos duermen en el sofá", language: "es"));

        var hits = await index.SearchAsync("gato", new SearchQueryOptions { Language = "es" });

        Assert.Contains(hits, h => h.Id == "doc-es");
    }
}
