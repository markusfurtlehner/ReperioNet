using ReperioNet.Languages.Ro;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>Romanian language pack: stemmer vectors, analyzer wiring and end-to-end search.</summary>
public class RomanianPackTests
{
    private readonly SnowballRomanianStemmer _stemmer = new();

    [Theory]
    // Verified against the official Snowball Romanian test data (snowballstem.org).
    // Note: the official algorithm stems "studenți" to "studenț" (final-vowel removal only).
    [InlineData("studenți", "studenț")]
    [InlineData("studenta", "student")]
    [InlineData("studentă", "student")]
    [InlineData("student", "student")]
    [InlineData("lucrează", "lucr")]
    [InlineData("lucrare", "lucr")]
    [InlineData("frumoasă", "frumoas")]
    [InlineData("frumoase", "frumoas")]
    [InlineData("românesc", "român")]
    [InlineData("românească", "român")]
    [InlineData("națională", "național")]
    [InlineData("băieți", "băi")]
    [InlineData("copiii", "copii")]
    [InlineData("copil", "copil")]
    public void Stem_KnownVectors(string token, string expected)
        => Assert.Equal(expected, _stemmer.Stem(token));

    [Theory]
    // Cedilla (U+015F/U+0163) spellings must stem identically to comma-below (U+0219/U+021B).
    [InlineData("studenți", "studenţi")]
    [InlineData("națională", "naţională")]
    [InlineData("acțiune", "acţiune")]
    public void Stem_CedillaAndCommaSpellings_ProduceSameStem(string comma, string cedilla)
        => Assert.Equal(_stemmer.Stem(comma), _stemmer.Stem(cedilla));

    [Theory]
    [InlineData("studenta", "studentă")]
    [InlineData("lucrează", "lucrare")]
    [InlineData("frumoasă", "frumoase")]
    [InlineData("românesc", "românească")]
    public void Stem_InflectionsOfSameLemma_ProduceSameStem(string first, string second)
        => Assert.Equal(_stemmer.Stem(first), _stemmer.Stem(second));

    [Theory]
    [InlineData("student")]
    [InlineData("studenț")]
    [InlineData("lucr")]
    [InlineData("român")]
    [InlineData("național")]
    public void Stem_IsIdempotent(string token)
        => Assert.Equal(_stemmer.Stem(token), _stemmer.Stem(_stemmer.Stem(token)));

    [Fact]
    public void Stem_EmptyAndShortTokens_AreSafe()
    {
        Assert.Equal(string.Empty, _stemmer.Stem(string.Empty));
        Assert.Equal("a", _stemmer.Stem("a"));
        Assert.Equal("de", _stemmer.Stem("de"));
        Assert.Equal("și", _stemmer.Stem("și"));
    }

    [Fact]
    public void Analyzer_IsWiredCorrectly()
    {
        var analyzer = new RomanianAnalyzer();

        Assert.Equal("ro", analyzer.LanguageCode);
        Assert.Null(analyzer.Phonetic);
        Assert.NotNull(analyzer.Stemmer);
        Assert.NotNull(analyzer.StopWords);
        Assert.True(analyzer.StopWords.IsStopWord("și"));   // comma-below spelling
        Assert.True(analyzer.StopWords.IsStopWord("şi"));   // cedilla spelling
        Assert.True(analyzer.StopWords.IsStopWord("pentru"));
        Assert.False(analyzer.StopWords.IsStopWord("student"));
    }

    [Fact]
    public async Task EndToEnd_StemmedSearchFindsOtherInflection()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddRomanian();
        });

        await index.AddAsync(TestOptions.Entry("doc-ro", "studenta citește o carte", language: "ro"));

        var hits = await index.SearchAsync("studentă", new SearchQueryOptions { Language = "ro" });

        Assert.Contains(hits, h => h.Id == "doc-ro");
    }
}
