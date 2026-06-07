using Xunit;

namespace ReperioNet.Tests;

public class RebuildOptimizeTests
{
    [Fact]
    public async Task Optimize_RunsAndIndexRemainsSearchable()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(Enumerable.Range(0, 20).Select(i => TestOptions.Entry($"doc-{i}", $"alpha {i}")));
        await index.OptimizeAsync();

        Assert.Equal(20, (await index.SearchAsync("alpha", new SearchQueryOptions { Limit = 100 })).Count);
    }

    [Fact]
    public async Task Optimize_WithTrigramDisabled_Runs()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.EnableTrigram = false);

        await index.AddAsync(TestOptions.Entry("doc", "alpha"));
        await index.OptimizeAsync();

        Assert.Single(await index.SearchAsync("alpha"));
    }

    [Fact]
    public async Task Rebuild_RepopulatesFtsFromStoredContent()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(Enumerable.Range(0, 10).Select(i => TestOptions.Entry($"doc-{i}", $"alpha token{i}")));
        await index.RebuildAsync();

        Assert.Equal(10, await index.CountAsync());
        Assert.Equal(10, (await index.SearchAsync("alpha", new SearchQueryOptions { Limit = 100 })).Count);
        Assert.Single(await index.SearchAsync("token7"));
        Assert.Equal(10, db.QueryScalarLong("SELECT COUNT(*) FROM documents_fts;"));
        Assert.Equal(10, db.QueryScalarLong("SELECT COUNT(*) FROM documents_trgm;"));
    }

    [Fact]
    public async Task Rebuild_StoreContentFalse_ReindexesFromRankText()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.StoreContent = false);

        await index.AddAsync(TestOptions.Entry("doc", "findable text"));
        await index.RebuildAsync();

        Assert.Single(await index.SearchAsync("findable"));
    }

    [Fact]
    public async Task Rebuild_OnEmptyIndex_Works()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.RebuildAsync();

        Assert.Equal(0, await index.CountAsync());
        Assert.Empty(await index.SearchAsync("anything"));
    }

    [Fact]
    public async Task Rebuild_PreservesUpsertBehaviorAfterwards()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "before rebuild"));
        await index.RebuildAsync();
        await index.AddAsync(TestOptions.Entry("doc", "after rebuild"));

        Assert.Equal(1, await index.CountAsync());
        Assert.Empty(await index.SearchAsync("before"));
        Assert.Single(await index.SearchAsync("after"));
    }
}
