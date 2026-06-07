using ReperioNet.Abstractions;
using ReperioNet.Languages.De;
using Xunit;

namespace ReperioNet.Languages.Tests;

public sealed class GermanAnalyzerTests
{
    [Fact]
    public void LanguageCode_IsDe()
        => Assert.Equal("de", new GermanAnalyzer().LanguageCode);

    [Fact]
    public void Pipeline_ProvidesStemmerPhoneticAndStopWords()
    {
        var analyzer = new GermanAnalyzer();

        Assert.IsType<SnowballGermanStemmer>(analyzer.Stemmer);
        Assert.NotNull(analyzer.Phonetic);
        Assert.IsType<KoelnerPhonetik>(analyzer.Phonetic);
        Assert.NotNull(analyzer.StopWords);
        Assert.IsType<GermanStopWords>(analyzer.StopWords);
    }

    [Theory]
    [InlineData("und")]
    [InlineData("der")]
    [InlineData("die")]
    [InlineData("ist")]
    [InlineData("für")]
    public void StopWords_ContainCommonFunctionWords(string token)
        => Assert.True(new GermanStopWords().IsStopWord(token));

    [Theory]
    [InlineData("rechnung")]
    [InlineData("müller")]
    [InlineData("haus")]
    public void StopWords_DoNotContainContentWords(string token)
        => Assert.False(new GermanStopWords().IsStopWord(token));

    [Fact]
    public async Task AddGerman_RegistersTheAnalyzerUnderDe()
    {
        using var db = new TestDatabase();
        ILanguageAnalyzer? registered = null;
        var chained = false;

        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            chained = ReferenceEquals(o, o.AddGerman());
            registered = o.Analyzers.Get("de");
        });

        Assert.True(chained);
        Assert.IsType<GermanAnalyzer>(registered);
    }
}
