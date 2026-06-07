using ReperioNet.Languages.All;
using ReperioNet.LanguageDetection;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>
/// PRD §11 (full version): a mixed de + en + fr corpus indexed WITHOUT explicit language codes —
/// the NTextCat detector resolves each document's language, and per-language stemming proves the
/// right analyzer was applied (the queries below only match via stems, never via base tokens).
/// </summary>
public class DetectionEndToEndTests
{
    private static readonly NTextCatDetector Detector = new();

    private static Task<SearchIndex<TestMeta>> OpenDetectingAsync(TestDatabase db)
        => TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false; // isolate stem matching
            o.AddAllEuropeanLanguages();
            o.LanguageDetector = Detector;
        });

    private static async Task SeedWithoutExplicitLanguagesAsync(SearchIndex<TestMeta> index)
        => await index.AddRangeAsync(
        [
            TestOptions.Entry("de-doc", "Die Rechnungen sind gestern angekommen und wurden von der Buchhaltung geprüft"),
            TestOptions.Entry("en-doc", "the invoices were received yesterday and they were carefully checked by the team"),
            TestOptions.Entry("fr-doc", "les chevaux galopent rapidement à travers la campagne française pendant l'été"),
        ]);

    [Fact]
    public async Task DetectedGerman_GetsGermanStemming()
    {
        using var db = new TestDatabase();
        await using var index = await OpenDetectingAsync(db);
        await SeedWithoutExplicitLanguagesAsync(index);

        // "Rechnung" only matches if the doc was indexed with the German stemmer
        // (stem of "Rechnungen"); the base token stream contains "rechnungen" only.
        var hits = await index.SearchAsync("Rechnung", new SearchQueryOptions { Language = "de" });

        Assert.Single(hits);
        Assert.Equal("de-doc", hits[0].Id);
    }

    [Fact]
    public async Task DetectedEnglish_GetsEnglishStemming()
    {
        using var db = new TestDatabase();
        await using var index = await OpenDetectingAsync(db);
        await SeedWithoutExplicitLanguagesAsync(index);

        // "invoice" stems to "invoic", the stem of the indexed "invoices".
        var hits = await index.SearchAsync("invoice", new SearchQueryOptions { Language = "en" });

        Assert.Single(hits);
        Assert.Equal("en-doc", hits[0].Id);
    }

    [Fact]
    public async Task DetectedFrench_GetsFrenchStemming()
    {
        using var db = new TestDatabase();
        await using var index = await OpenDetectingAsync(db);
        await SeedWithoutExplicitLanguagesAsync(index);

        // "cheval" matches the stem of the indexed plural "chevaux".
        var hits = await index.SearchAsync("cheval", new SearchQueryOptions { Language = "fr" });

        Assert.Single(hits);
        Assert.Equal("fr-doc", hits[0].Id);
    }

    [Fact]
    public async Task ExplicitEntryLanguage_StillWinsOverDetection()
    {
        using var db = new TestDatabase();
        await using var index = await OpenDetectingAsync(db);

        // The text is German, but the explicit "en" code must win (§6.3 resolution order),
        // so the German inflection match must NOT work.
        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnungen sind angekommen und wurden geprüft", "en"));

        Assert.Empty(await index.SearchAsync("Rechnung", new SearchQueryOptions { Language = "de" }));
    }
}
