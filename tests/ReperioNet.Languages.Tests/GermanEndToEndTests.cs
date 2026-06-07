using ReperioNet.Languages.De;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>
/// PRD §11 end-to-end cases for the German pack. Trigram is disabled so the assertions genuinely
/// exercise the stem and phonetic columns rather than substring recall.
/// </summary>
public sealed class GermanEndToEndTests
{
    private static Task<SearchIndex<TestMeta>> OpenGermanAsync(TestDatabase db)
        => TestOptions.OpenAsync(db, o =>
        {
            o.AddGerman();
            o.EnableTrigram = false;
            o.DefaultLanguage = "de";
        });

    [Fact]
    public async Task Stemming_InflectedQuery_FindsTheDocument()
    {
        using var db = new TestDatabase();
        await using var index = await OpenGermanAsync(db);
        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen", "de"));

        var hits = await index.SearchAsync("Rechnungen");

        Assert.Contains(hits, hit => hit.Id == "doc");
    }

    [Fact]
    public async Task Phonetic_AsciiQuery_FindsUmlautDocument()
    {
        using var db = new TestDatabase();
        await using var index = await OpenGermanAsync(db);
        await index.AddAsync(TestOptions.Entry("doc", "Herr Müller", "de"));

        // "mueller" and "müller" agree neither on the base token nor on the stem
        // ("muell" vs "mull"); only the Kölner Phonetik code "657" links them.
        var hits = await index.SearchAsync("Mueller");

        Assert.Contains(hits, hit => hit.Id == "doc");
    }

    [Fact]
    public async Task Phonetic_UmlautQuery_FindsAsciiDocument()
    {
        using var db = new TestDatabase();
        await using var index = await OpenGermanAsync(db);
        await index.AddAsync(TestOptions.Entry("doc", "Herr Mueller", "de"));

        var hits = await index.SearchAsync("Müller");

        Assert.Contains(hits, hit => hit.Id == "doc");
    }

    [Fact]
    public async Task Phonetic_IsTheOnlyLink_BetweenMuellerSpellings()
    {
        using var db = new TestDatabase();
        await using var index = await OpenGermanAsync(db);
        await index.AddAsync(TestOptions.Entry("doc", "Herr Müller", "de"));

        // Sanity for the two tests above: with the phonetic column excluded from the
        // query, the spelling variants no longer match at all.
        var hits = await index.SearchAsync(
            "Mueller",
            new SearchQueryOptions { EnablePhonetic = false });

        Assert.DoesNotContain(hits, hit => hit.Id == "doc");
    }
}
