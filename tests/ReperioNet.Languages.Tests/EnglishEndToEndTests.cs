using ReperioNet.Languages.En;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>
/// PRD §11 mandatory end-to-end cases for the English pack: stemming ("running" finds "run")
/// and Double Metaphone phonetics ("Smyth" finds "Smith"). The trigram index is disabled so
/// the stem/phonetic columns alone must provide the match.
/// </summary>
public class EnglishEndToEndTests
{
    [Fact]
    public async Task Stemming_QueryRunningFindsDocumentContainingRun()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddEnglish();
        });

        await index.AddAsync(TestOptions.Entry("doc", "they run daily", language: "en"));

        // "running" stems to "run", matching the indexed stem; the base token does not occur.
        var hits = await index.SearchAsync("running", new SearchQueryOptions { Language = "en" });

        var hit = Assert.Single(hits);
        Assert.Equal("doc", hit.Id);

        // An unrelated inflection finds nothing.
        Assert.Empty(await index.SearchAsync("walking", new SearchQueryOptions { Language = "en" }));
    }

    [Fact]
    public async Task Phonetic_QuerySmythFindsDocumentContainingSmith()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.AddEnglish();
        });

        await index.AddAsync(TestOptions.Entry("doc", "Mr Smith arrived", language: "en"));

        // "smyth" and "smith" share neither base token nor stem; only the Double Metaphone
        // code (SM0) matches, so this genuinely exercises the phonetic column.
        var hits = await index.SearchAsync("Smyth", new SearchQueryOptions { Language = "en" });

        var hit = Assert.Single(hits);
        Assert.Equal("doc", hit.Id);

        // With the phonetic column excluded from the query the match disappears.
        Assert.Empty(await index.SearchAsync(
            "Smyth", new SearchQueryOptions { Language = "en", EnablePhonetic = false }));
    }
}
