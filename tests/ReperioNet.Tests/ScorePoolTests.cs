using Xunit;

namespace ReperioNet.Tests;

/// <summary>
/// <see cref="SearchQueryOptions.ScorePoolSize"/>: only the best candidates are scored against their stored text; the rest of the
/// pool is still returned, behind them, without its text being loaded.
/// </summary>
public class ScorePoolTests
{
    private static async Task<SearchIndex<TestMeta>> SeedAsync(TestDatabase db)
    {
        var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableStemming = false;
            o.StoreContent = true;
        });
        // Distinct term frequencies give every document a distinct bm25 rank: doc-10 is the best.
        await index.AddRangeAsync(Enumerable.Range(1, 10).Select(i =>
            TestOptions.Entry($"doc-{i:D2}", string.Join(' ', Enumerable.Repeat("sigma", i)) + " padding words")));
        return index;
    }

    [Fact]
    public async Task EveryCandidateIsReturned_TheBestAreScoredAndTheTailFollows()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        var all = await index.SearchAsync("sigma", new SearchQueryOptions { Limit = 100, IncludeSnippet = true });
        var hits = await index.SearchAsync("sigma", new SearchQueryOptions { Limit = 100, IncludeSnippet = true, ScorePoolSize = 3 });

        Assert.Equal(all.Select(h => h.Id).Order(), hits.Select(h => h.Id).Order());
        // The three best by bm25 are scored against their text, like without the option, and have a snippet.
        var scored = hits.Take(3).ToList();
        Assert.Equal(new[] { "doc-08", "doc-09", "doc-10" }, scored.Select(h => h.Id).Order());
        Assert.All(scored, h => Assert.Equal(all.Single(a => a.Id == h.Id).Score, h.Score));
        Assert.All(scored, h => Assert.NotNull(h.Snippet));
        // The tail: bm25 share only, no snippet, in bm25 order behind the scored ones.
        var tail = hits.Skip(3).ToList();
        Assert.All(tail, h => Assert.Null(h.Snippet));
        Assert.All(tail, h => Assert.InRange(h.Score, 0.0, 0.4));
        Assert.Equal(new[] { "doc-07", "doc-06", "doc-05", "doc-04", "doc-03", "doc-02", "doc-01" }, tail.Select(h => h.Id));
        Assert.All(tail, h => Assert.Equal(all.Single(a => a.Id == h.Id).Metadata, h.Metadata));
    }

    [Fact]
    public async Task MinScore_DoesNotDropTheTail_AndPagingRunsOverTheWholePool()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        var options = new SearchQueryOptions { Limit = 4, Offset = 2, MinScore = 0.5, ScorePoolSize = 3 };
        var page = await index.SearchAsync("sigma", options);
        options.Offset = 0;
        options.Limit = 100;
        var whole = await index.SearchAsync("sigma", options);

        Assert.Equal(10, whole.Count);
        Assert.Equal(whole.Skip(2).Take(4).Select(h => h.Id), page.Select(h => h.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(100)]
    public async Task WithoutTheOption_OrWithOneAsLargeAsThePool_NothingChanges(int? scorePool)
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        var before = await index.SearchAsync("sigma", new SearchQueryOptions { Limit = 100, IncludeSnippet = true });
        var after = await index.SearchAsync("sigma", new SearchQueryOptions { Limit = 100, IncludeSnippet = true, ScorePoolSize = scorePool });

        Assert.Equal(before.Select(h => (h.Id, h.Score, h.Snippet)), after.Select(h => (h.Id, h.Score, h.Snippet)));
    }
}
