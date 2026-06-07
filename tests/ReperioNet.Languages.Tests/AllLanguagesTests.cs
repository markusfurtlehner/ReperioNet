using ReperioNet.Abstractions;
using ReperioNet.Languages.All;
using Xunit;

namespace ReperioNet.Languages.Tests;

public class AllLanguagesTests
{
    private static readonly string[] AllCodes =
        ["de", "en", "fr", "es", "it", "pt", "nl", "sv", "no", "da", "fi", "ru", "hu", "ro", "tr"];

    [Fact]
    public async Task AddAllEuropeanLanguages_RegistersAllFifteenAnalyzers()
    {
        using var db = new TestDatabase();
        IAnalyzerProvider? analyzers = null;

        await using (await TestOptions.OpenAsync(db, o =>
        {
            o.AddAllEuropeanLanguages();
            analyzers = o.Analyzers;
        }))
        {
        }

        Assert.NotNull(analyzers);
        Assert.All(AllCodes, code => Assert.NotNull(analyzers.Get(code)));
    }

    [Fact]
    public async Task AddAllEuropeanLanguages_ReturnsOptionsForChaining()
    {
        using var db = new TestDatabase();
        var chained = false;

        await using (await TestOptions.OpenAsync(db, o => chained = ReferenceEquals(o.AddAllEuropeanLanguages(), o)))
        {
        }

        Assert.True(chained);
    }

    [Fact]
    public async Task AddAllEuropeanLanguages_OnlyDeAndEnHavePhonetics()
    {
        using var db = new TestDatabase();
        IAnalyzerProvider? analyzers = null;

        await using (await TestOptions.OpenAsync(db, o =>
        {
            o.AddAllEuropeanLanguages();
            analyzers = o.Analyzers;
        }))
        {
        }

        Assert.NotNull(analyzers!.Get("de")!.Phonetic);
        Assert.NotNull(analyzers.Get("en")!.Phonetic);
        Assert.All(
            AllCodes.Where(code => code is not ("de" or "en")),
            code => Assert.Null(analyzers.Get(code)!.Phonetic));
    }

    [Fact]
    public async Task AddAllEuropeanLanguages_EveryAnalyzerHasStemmerAndStopWords()
    {
        using var db = new TestDatabase();
        IAnalyzerProvider? analyzers = null;

        await using (await TestOptions.OpenAsync(db, o =>
        {
            o.AddAllEuropeanLanguages();
            analyzers = o.Analyzers;
        }))
        {
        }

        Assert.All(AllCodes, code =>
        {
            var analyzer = analyzers!.Get(code)!;
            Assert.Equal(code, analyzer.LanguageCode);
            Assert.NotNull(analyzer.Stemmer);
            Assert.NotNull(analyzer.StopWords);
        });
    }
}
