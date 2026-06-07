using ReperioNet.Languages.De;
using ReperioNet.Languages.En;
using ReperioNet.Languages.Fr;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>
/// PRD §11: a mixed German + English + French corpus in one index, with per-language stemming.
/// (Automatic language detection arrives with ReperioNet.LanguageDetection in Milestone 7; here the
/// entries carry explicit language codes.)
/// </summary>
public class MultilingualCorpusTests
{
    private static Task<SearchIndex<TestMeta>> OpenMixedAsync(TestDatabase db)
        => TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false; // isolate per-language stem matching
            o.AddGerman().AddEnglish().AddFrench();
        });

    private static async Task SeedAsync(SearchIndex<TestMeta> index)
        => await index.AddRangeAsync(
        [
            TestOptions.Entry("de-doc", "Die Rechnung ist angekommen", "de"),
            TestOptions.Entry("en-doc", "they run daily to the office", "en"),
            TestOptions.Entry("fr-doc", "le cheval est rapide", "fr"),
        ]);

    [Fact]
    public async Task GermanQuery_StemsWithGermanAnalyzer()
    {
        using var db = new TestDatabase();
        await using var index = await OpenMixedAsync(db);
        await SeedAsync(index);

        var hits = await index.SearchAsync("Rechnungen", new SearchQueryOptions { Language = "de" });

        Assert.Single(hits);
        Assert.Equal("de-doc", hits[0].Id);
    }

    [Fact]
    public async Task EnglishQuery_StemsWithEnglishAnalyzer()
    {
        using var db = new TestDatabase();
        await using var index = await OpenMixedAsync(db);
        await SeedAsync(index);

        var hits = await index.SearchAsync("running", new SearchQueryOptions { Language = "en" });

        Assert.Single(hits);
        Assert.Equal("en-doc", hits[0].Id);
    }

    [Fact]
    public async Task FrenchQuery_StemsWithFrenchAnalyzer()
    {
        using var db = new TestDatabase();
        await using var index = await OpenMixedAsync(db);
        await SeedAsync(index);

        var hits = await index.SearchAsync("chevaux", new SearchQueryOptions { Language = "fr" });

        Assert.Single(hits);
        Assert.Equal("fr-doc", hits[0].Id);
    }

    [Fact]
    public async Task UnregisteredLanguage_FallsBackGracefully()
    {
        using var db = new TestDatabase();
        await using var index = await OpenMixedAsync(db);
        await index.AddAsync(TestOptions.Entry("it-doc", "contenuto importante", "it"));

        // No "it" analyzer registered: the identity fallback indexes base tokens only.
        var hits = await index.SearchAsync("contenuto", new SearchQueryOptions { Language = "it" });

        Assert.Single(hits);
        Assert.Equal("it-doc", hits[0].Id);
    }

    [Fact]
    public async Task ChainedRegistration_AllThreeAnalyzersActive()
    {
        using var db = new TestDatabase();
        await using var index = await OpenMixedAsync(db);
        await SeedAsync(index);

        // One inflected query per language — each must resolve through its own stemmer.
        Assert.Single(await index.SearchAsync("Rechnungen", new SearchQueryOptions { Language = "de" }));
        Assert.Single(await index.SearchAsync("running", new SearchQueryOptions { Language = "en" }));
        Assert.Single(await index.SearchAsync("chevaux", new SearchQueryOptions { Language = "fr" }));
    }
}
