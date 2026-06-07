using Xunit;

namespace ReperioNet.Tests;

/// <summary>AllTerms default + automatic AnyTerms fallback (§9.5 TermMatch).</summary>
public class TermMatchTests
{
    private static async Task<SearchIndex<TestMeta>> SeedAsync(TestDatabase db)
    {
        var index = await TestOptions.OpenAsync(db);
        await index.AddRangeAsync(
        [
            TestOptions.Entry("both-1", "kafka prozess gestartet"),
            TestOptions.Entry("both-2", "der kafka prozess wurde geprüft und der prozess lief"),
            TestOptions.Entry("only-kafka", "kafka allein im büro"),
            TestOptions.Entry("only-prozess", "prozess allein gestartet"),
        ]);
        return index;
    }

    [Fact]
    public async Task AllTerms_WithEnoughIntersection_ReturnsOnlyDocsContainingAllTerms()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        // Limit == intersection size: the strict pass satisfies the page, no fallback widening.
        var hits = await index.SearchAsync("kafka prozess", new SearchQueryOptions { Limit = 2 });

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.StartsWith("both-", h.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllTerms_UnderLimit_WidensViaAnyTermsFallback()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        // Default Limit (50) > 2 all-terms matches: the fallback appends any-term docs.
        var hits = await index.SearchAsync("kafka prozess");

        Assert.Equal(4, hits.Count);
        Assert.Contains(hits, h => h.Id == "only-kafka");
        Assert.Contains(hits, h => h.Id == "only-prozess");
    }

    [Fact]
    public async Task AllTerms_FallbackHits_RankAfterAllTermsHits()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        var hits = await index.SearchAsync("kafka prozess");

        // The two all-terms documents occupy the top positions regardless of fuzzy scores.
        Assert.All(hits.Take(2), h => Assert.StartsWith("both-", h.Id, StringComparison.Ordinal));
        Assert.All(hits.Skip(2), h => Assert.StartsWith("only-", h.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllTerms_EmptyIntersection_FallsBackTransparently()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        // No document contains both "kafka" and "fehlt": pure fallback recall over the three
        // documents that contain "kafka".
        var hits = await index.SearchAsync("kafka fehlt");

        Assert.Equal(3, hits.Count);
        Assert.Contains(hits, h => h.Id == "both-1");
        Assert.Contains(hits, h => h.Id == "both-2");
        Assert.Contains(hits, h => h.Id == "only-kafka");
    }

    [Fact]
    public async Task AnyTerms_ReproducesWideRecall_WithScoreOrdering()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        var hits = await index.SearchAsync(
            "kafka prozess",
            new SearchQueryOptions { TermMatch = TermMatch.AnyTerms });

        // All four docs match, in one flat tier ordered purely by score.
        Assert.Equal(4, hits.Count);
        for (var i = 1; i < hits.Count; i++)
        {
            Assert.True(hits[i - 1].Score >= hits[i].Score, "AnyTerms results must be ordered by score alone");
        }
    }

    [Fact]
    public async Task AllTerms_SingleToken_BehavesLikeBefore()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        var allTerms = await index.SearchAsync("kafka");
        var anyTerms = await index.SearchAsync("kafka", new SearchQueryOptions { TermMatch = TermMatch.AnyTerms });

        Assert.Equal(anyTerms.Select(h => h.Id), allTerms.Select(h => h.Id));
    }

    [Fact]
    public async Task AllTerms_StemVariants_StillRecalledThroughFallback()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.Analyzers.Register(TestAnalyzerFactory.GermanSuffixStripper());
            o.DefaultLanguage = "de";
        });

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnungen wurden geprüft", "de"));

        // Neither literal base token matches ("rechnung" vs "rechnungen"), so the strict pass is
        // empty and the fallback's stem clause recalls the inflected document.
        Assert.Single(await index.SearchAsync("rechnung geprüft", new SearchQueryOptions { Language = "de" }));
    }

    [Fact]
    public async Task AllTerms_OffsetPagesAcrossTiers()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        var page = await index.SearchAsync("kafka prozess", new SearchQueryOptions { Offset = 2, Limit = 10 });

        // Skipping the two all-terms hits lands on the fallback tier.
        Assert.Equal(2, page.Count);
        Assert.All(page, h => Assert.StartsWith("only-", h.Id, StringComparison.Ordinal));
    }
}
