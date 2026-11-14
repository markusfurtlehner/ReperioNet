using Xunit;

namespace ReperioNet.Tests;

/// <summary>
/// A query with many matches (2.5, 2.5.1) gathers its trigram candidates, reads its unscored tail and builds its scores and snippets
/// side by side. The answer must not depend on that: every match once, in the same order on every call.
/// </summary>
public class ParallelSearchTests
{
    private const int Documents = 1500;

    private static async Task<SearchIndex<TestMeta>> SeedAsync(TestDatabase db)
    {
        var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableStemming = false;
            o.StoreContent = true;
        });
        await index.AddRangeAsync(Enumerable.Range(1, Documents).Select(i =>
            TestOptions.Entry($"doc-{i:D4}", string.Join(' ', Enumerable.Repeat("omega", 1 + (i % 7))) + $" filler text number {i}")));
        return index;
    }

    [Theory]
    [InlineData(100)]
    [InlineData(400)]
    [InlineData(null)]
    public async Task EveryMatchComesOnce_InTheSameOrderOnEveryCall(int? scorePool)
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);
        var options = new SearchQueryOptions { Limit = 5000, CandidatePoolSize = 5000, IncludeSnippet = true, ScorePoolSize = scorePool };

        var first = await index.SearchMatchesAsync("omega", options);
        var second = await index.SearchMatchesAsync("omega", options);
        var hits = await index.SearchAsync("omega", options);

        Assert.Equal(Documents, first.Count);
        Assert.Equal(Documents, first.Select(m => m.Id).Distinct().Count());
        Assert.Equal(first.Select(m => (m.Id, m.Score, m.Snippet)), second.Select(m => (m.Id, m.Score, m.Snippet)));
        Assert.Equal(first.Select(m => (m.Id, m.Score, m.Snippet)), hits.Select(h => (h.Id, h.Score, h.Snippet)));

        var scored = scorePool ?? Documents;
        Assert.Equal(scored, first.Count(m => m.Snippet is not null));
        Assert.All(first.Where(m => m.Snippet is not null), m => Assert.Contains("omega", m.Snippet!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchesThatRunAtTheSameTimeAgree()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);
        var options = new SearchQueryOptions { Limit = 5000, CandidatePoolSize = 5000, ScorePoolSize = 100 };

        var expected = (await index.SearchMatchesAsync("omega", options)).Select(m => m.Id).ToList();
        var all = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => index.SearchMatchesAsync("omega", options))));

        Assert.All(all, matches => Assert.Equal(expected, matches.Select(m => m.Id)));
    }
}
