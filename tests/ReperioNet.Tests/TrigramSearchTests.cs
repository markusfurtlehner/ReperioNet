using Xunit;

namespace ReperioNet.Tests;

public class TrigramSearchTests
{
    [Fact]
    public async Task Substring_ViaTrigram_FindsDocument()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen"));

        // "chnun" is no base token — only the trigram table can recall it.
        var hits = await index.SearchAsync("chnun");

        Assert.Single(hits);
        Assert.Equal("doc", hits[0].Id);
    }

    [Fact]
    public async Task Substring_IsCaseInsensitive()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen"));

        Assert.Single(await index.SearchAsync("ECHNUN"));
    }

    [Fact]
    public async Task Substring_TruncatedWord_FindsDocument()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen"));

        // A dropped trailing letter still leaves a matching contiguous fragment.
        Assert.Single(await index.SearchAsync("Rechnun"));
    }

    [Fact]
    public async Task Substring_WithTrigramDisabled_NoHits()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.EnableTrigram = false);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen"));

        Assert.Empty(await index.SearchAsync("chnun"));
        Assert.Single(await index.SearchAsync("rechnung"));
    }

    [Fact]
    public async Task MergedRecall_TokenAndSubstringSources_BothReturned()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("token-doc", "the warehouse staff"),
            TestOptions.Entry("substring-doc", "smartwarehouseunit inventory"),
        ]);

        // "warehouse" base-matches token-doc and substring-matches substring-doc.
        var hits = await index.SearchAsync("warehouse");

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Id == "token-doc");
        Assert.Contains(hits, h => h.Id == "substring-doc");
    }

    [Fact]
    public async Task MergedRecall_SameDocFromBothSources_SingleHit()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen"));

        // "rechnung" matches via base token AND trigram substring; merge by rowid must dedupe.
        Assert.Single(await index.SearchAsync("rechnung"));
    }

    [Fact]
    public async Task ShortQuery_PrefixAid_MatchesTokenPrefix()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen"));

        // 1-2 char queries skip trigram but get an FTS5 prefix term on the last base token (§9.5).
        Assert.Single(await index.SearchAsync("re"));
        Assert.Single(await index.SearchAsync("r"));
    }

    [Fact]
    public async Task ShortQuery_TrigramIsSkipped_NoMidWordSubstringMatch()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("prefix-doc", "rebuild planned"),
            TestOptions.Entry("midword-doc", "warehouse only"),
        ]);

        // Query length 2: trigram skipped (§10). "re" prefix-matches "rebuild" but must NOT
        // substring-match the mid-word "re" in "warehouse".
        var hits = await index.SearchAsync("re");

        Assert.Single(hits);
        Assert.Equal("prefix-doc", hits[0].Id);
    }

    [Fact]
    public async Task ShortQuery_ExactShortToken_StillMatches()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "ab cd ef"));

        Assert.Single(await index.SearchAsync("ab"));
    }

    [Fact]
    public async Task CandidatePoolSize_CapsTheResultSet()
    {
        using var db = new TestDatabase();
        // Stemming off: the deduped stem stream (tf=1 per doc) would otherwise add a
        // length-dependent bm25 term that breaks the monotonic tf ladder below.
        await using var index = await TestOptions.OpenAsync(db, o => o.EnableStemming = false);

        // Distinct term frequencies give every doc a distinct bm25 rank. Fuzzy off and a second
        // absent token ("tau") keep scores pure normalized bm25 (no boost, no 1.0 tie-cap).
        await index.AddRangeAsync(Enumerable.Range(1, 10).Select(i =>
            TestOptions.Entry($"doc-{i}", string.Join(' ', Enumerable.Repeat("sigma", i)) + " padding words")));

        var hits = await index.SearchAsync(
            "sigma tau",
            new SearchQueryOptions { CandidatePoolSize = 5, Limit = 100, EnableFuzzy = false });

        // The pool keeps only the 5 best-bm25 candidates (§9.7).
        Assert.Equal(5, hits.Count);
        Assert.Equal(new[] { "doc-10", "doc-9", "doc-8", "doc-7", "doc-6" }, hits.Select(h => h.Id));
    }

    [Fact]
    public async Task TrigramOnlyHit_MetadataRoundTrips()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        var meta = new TestMeta("substring source", 7);
        await index.AddAsync(new SearchEntry<TestMeta>("doc", "smartwarehouseunit", meta));

        var hits = await index.SearchAsync("warehouse");

        Assert.Single(hits);
        Assert.Equal(meta, hits[0].Metadata);

        // Sole candidate: normBm25 = 1.0, blended with its fuzzy similarity plus the substring boost.
        Assert.InRange(hits[0].Score, 0.5, 1.0);
    }

    [Fact]
    public async Task Substring_AfterUpsert_ReflectsNewContent()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "smartwarehouseunit"));
        await index.AddAsync(TestOptions.Entry("doc", "completely different"));

        Assert.Empty(await index.SearchAsync("warehouse"));
        Assert.Single(await index.SearchAsync("differen"));
    }

    [Fact]
    public async Task Substring_AfterRebuild_StillWorks()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen"));
        await index.RebuildAsync();

        Assert.Single(await index.SearchAsync("chnun"));
    }
}
