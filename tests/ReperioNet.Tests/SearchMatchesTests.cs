using Xunit;

namespace ReperioNet.Tests;

/// <summary>
/// <c>SearchMatchesAsync</c> (2.4): the same matches as <c>SearchAsync</c>, without metadata; the ids of the candidates that are not
/// scored come from the slim <c>documents_key</c> index.
/// </summary>
public class SearchMatchesTests
{
    private static async Task<SearchIndex<TestMeta>> SeedAsync(TestDatabase db)
    {
        var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableStemming = false;
            o.StoreContent = true;
        });
        await index.AddRangeAsync(Enumerable.Range(1, 10).Select(i =>
            TestOptions.Entry($"doc-{i:D2}", string.Join(' ', Enumerable.Repeat("sigma", i)) + " padding words")));
        return index;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(100)]
    public async Task TheMatchesAreTheHits_WithoutMetadata(int? scorePool)
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);
        var options = new SearchQueryOptions { Limit = 100, IncludeSnippet = true, ScorePoolSize = scorePool };

        var hits = await index.SearchAsync("sigma", options);
        var matches = await index.SearchMatchesAsync("sigma", options);

        Assert.Equal(10, matches.Count);
        Assert.Equal(hits.Select(h => (h.Id, h.Score, h.Snippet)), matches.Select(m => (m.Id, m.Score, m.Snippet)));
    }

    [Fact]
    public async Task PagingAndEmptyQueriesBehaveLikeSearch()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);
        var options = new SearchQueryOptions { Limit = 4, Offset = 2, ScorePoolSize = 3 };

        Assert.Equal((await index.SearchAsync("sigma", options)).Select(h => h.Id), (await index.SearchMatchesAsync("sigma", options)).Select(m => m.Id));
        Assert.Empty(await index.SearchMatchesAsync("  "));
        Assert.Empty(await index.SearchMatchesAsync("nothinghere", options));
    }

    [Fact]
    public async Task TheKeyIndexFollowsWrites_AndAnIndexFromAnEarlierVersionGetsItAtItsNextOpen()
    {
        using var db = new TestDatabase();
        var options = new SearchQueryOptions { Limit = 100, ScorePoolSize = 0 };
        await using (var index = await SeedAsync(db))
        {
            await index.RemoveAsync("doc-03");
            await index.AddAsync(TestOptions.Entry("doc-11", "sigma once more"));
            var ids = (await index.SearchMatchesAsync("sigma", options)).Select(m => m.Id).Order().ToList();
            Assert.DoesNotContain("doc-03", ids);
            Assert.Contains("doc-11", ids);
            Assert.Equal(10, ids.Count);
        }

        // An index written before 2.4 has no documents_key.
        using (var raw = db.OpenRaw())
        {
            using var drop = raw.CreateCommand();
            drop.CommandText = "DROP INDEX documents_key;";
            drop.ExecuteNonQuery();
        }

        await using (var reopened = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableStemming = false;
            o.StoreContent = true;
        }))
        {
            Assert.Equal(10, (await reopened.SearchMatchesAsync("sigma", options)).Count);
        }

        using (var raw = db.OpenRaw())
        {
            using var check = raw.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'documents_key';";
            Assert.Equal(1L, check.ExecuteScalar());
        }
    }
}
