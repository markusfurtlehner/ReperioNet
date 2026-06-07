using ReperioNet;
using ReperioNet.Languages.All;
using ReperioNet.Sample.ConsoleApp;

// ReperioNet end-to-end demo: index a small multilingual corpus, then run queries that exercise
// stemming, phonetic matching, substring recall and snippets. Doubles as the trimmed-publish
// AOT smoke test — it must run unchanged when published with PublishTrimmed=true.

var databasePath = Path.Combine(Path.GetTempPath(), $"reperionet-sample-{Guid.NewGuid():N}.db");
try
{
    await using var index = await SearchIndex<DocMeta>.OpenAsync(databasePath, o =>
    {
        o.MetadataTypeInfo = SampleJsonContext.Default.DocMeta;   // source-generated, required
        o.AddAllEuropeanLanguages();                              // all fifteen language packs
        o.DefaultLanguage = "en";
    });

    await index.AddRangeAsync(
    [
        new SearchEntry<DocMeta>(
            "de-1",
            "Die Rechnungen von Herrn Müller sind gestern angekommen.",
            new DocMeta("Rechnungseingang", "docs/de-1.txt"),
            "de"),
        new SearchEntry<DocMeta>(
            "en-1",
            "The quarterly invoices were checked while running the audit.",
            new DocMeta("Quarterly audit", "docs/en-1.txt"),
            "en"),
        new SearchEntry<DocMeta>(
            "fr-1",
            "Les chevaux galopent à travers la campagne française.",
            new DocMeta("Chevaux au galop", "docs/fr-1.txt"),
            "fr"),
    ]);

    Console.WriteLine($"Indexed {await index.CountAsync()} documents into {databasePath}");
    Console.WriteLine();

    var failures = 0;
    failures += await RunQueryAsync(index, "Rechnung", "de", expectId: "de-1");   // German stemming: Rechnung ~ Rechnungen
    failures += await RunQueryAsync(index, "Mueller", "de", expectId: "de-1");    // Kölner Phonetik: Mueller ~ Müller
    failures += await RunQueryAsync(index, "run", "en", expectId: "en-1");        // English stemming: run ~ running
    failures += await RunQueryAsync(index, "cheval", "fr", expectId: "fr-1");     // French stemming: cheval ~ chevaux
    failures += await RunQueryAsync(index, "galop", null, expectId: "fr-1");      // trigram substring: galop ⊂ galopent

    if (failures > 0)
    {
        Console.WriteLine($"SMOKE TEST FAILED: {failures} quer{(failures == 1 ? "y" : "ies")} missed the expected document.");
        return 1;
    }

    Console.WriteLine("All demo queries returned the expected documents. OK");
    return 0;
}
finally
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        File.Delete(databasePath + suffix);
    }
}

static async Task<int> RunQueryAsync(SearchIndex<DocMeta> index, string query, string? language, string expectId)
{
    var hits = await index.SearchAsync(query, new SearchQueryOptions
    {
        Language = language,
        IncludeSnippet = true,
    });

    Console.WriteLine($"query \"{query}\" (language: {language ?? "default"}) -> {hits.Count} hit(s)");
    foreach (var hit in hits)
    {
        Console.WriteLine($"  {hit.Score:F2}  [{hit.Id}] {hit.Metadata.Title}");
        if (hit.Snippet is not null)
        {
            Console.WriteLine($"        {hit.Snippet}");
        }
    }

    Console.WriteLine();
    return hits.Any(h => h.Id == expectId) ? 0 : 1;
}
