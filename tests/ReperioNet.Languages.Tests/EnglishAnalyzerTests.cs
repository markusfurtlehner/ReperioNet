using ReperioNet.Languages.En;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>Shape and registration of the English analyzer.</summary>
public class EnglishAnalyzerTests
{
    [Fact]
    public void Analyzer_ExposesTheEnglishPipeline()
    {
        var analyzer = new EnglishAnalyzer();

        Assert.Equal("en", analyzer.LanguageCode);
        Assert.IsType<SnowballEnglishStemmer>(analyzer.Stemmer);
        Assert.NotNull(analyzer.Phonetic);
        Assert.IsType<DoubleMetaphone>(analyzer.Phonetic);
        Assert.NotNull(analyzer.StopWords);
        Assert.IsType<EnglishStopWords>(analyzer.StopWords);
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("of")]
    [InlineData("is")]
    [InlineData("they")]
    public void StopWords_RecognizeEnglishFunctionWords(string token)
    {
        var stopWords = new EnglishStopWords();
        Assert.True(stopWords.IsStopWord(token));
    }

    [Theory]
    [InlineData("invoice")]
    [InlineData("run")]
    [InlineData("smith")]
    [InlineData("")]
    public void StopWords_DoNotFlagContentWords(string token)
    {
        var stopWords = new EnglishStopWords();
        Assert.False(stopWords.IsStopWord(token));
    }

    [Fact]
    public async Task AddEnglish_RegistersTheAnalyzerAndReturnsTheOptions()
    {
        using var db = new TestDatabase();
        ReperioOptions<TestMeta>? captured = null;
        ReperioOptions<TestMeta>? returned = null;

        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            captured = o;
            returned = o.AddEnglish();
        });

        Assert.NotNull(captured);
        Assert.Same(captured, returned);
        Assert.IsType<EnglishAnalyzer>(captured.Analyzers.Get("en"));
    }
}
