using Xunit;

namespace ReperioNet.Tests;

/// <summary>
/// 2.6: a query with more matches than <see cref="SearchQueryOptions.RankLimit"/> is answered as an unranked list, and
/// <c>ScoreMatchesAsync</c> scores the matches the caller picks. A query within the limit is answered exactly as without it.
/// </summary>
public class RankLimitTests
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
            TestOptions.Entry($"doc-{i:D4}", string.Join(' ', Enumerable.Repeat("omega", 1 + (i % 7))) + $" filler text number {i}" + (i % 100 == 0 ? " kiwi" : ""))));
        return index;
    }

    private static SearchQueryOptions Options(int? rankLimit) =>
        new() { Limit = 5000, CandidatePoolSize = 5000, IncludeSnippet = true, ScorePoolSize = 100, RankLimit = rankLimit };

    [Fact]
    public async Task AQueryOverTheLimit_ListsEveryMatchOnce_Unranked()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        var matches = await index.SearchMatchesAsync("omega", Options(rankLimit: 200));
        var hits = await index.SearchAsync("omega", Options(rankLimit: 200));

        Assert.Equal(Documents, matches.Count);
        Assert.Equal(Documents, matches.Select(m => m.Id).Distinct().Count());
        Assert.All(matches, m => Assert.Equal((0d, (string?)null), (m.Score, m.Snippet)));
        Assert.Equal(matches.Select(m => m.Id), hits.Select(h => h.Id));
        Assert.All(hits, h => Assert.NotNull(h.Metadata));
    }

    [Fact]
    public async Task AQueryOverTheLimit_HonoursPoolOffsetAndLimit()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);
        var all = (await index.SearchMatchesAsync("omega", Options(rankLimit: 200))).Select(m => m.Id).ToList();

        var options = Options(rankLimit: 200);
        options.CandidatePoolSize = 600;
        options.Offset = 50;
        options.Limit = 100;
        var page = await index.SearchMatchesAsync("omega", options);

        Assert.Equal(all.Skip(50).Take(100), page.Select(m => m.Id));
    }

    [Fact]
    public async Task ALongUnrankedList_ReadInParts_ListsEveryMatchOnceInItsOrder()
    {
        // 2.6.1: from 8,192 listed matches on, the ids are read in parts on several connections.
        const int many = 20_000;
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableStemming = false;
            o.StoreContent = true;
        });
        await index.AddRangeAsync(Enumerable.Range(1, many).Select(i => TestOptions.Entry($"doc-{i:D5}", $"omega filler {i}")));
        var options = new SearchQueryOptions { Limit = 50_000, CandidatePoolSize = 50_000, RankLimit = 200 };

        var matches = await index.SearchMatchesAsync("omega", options);

        Assert.Equal(Enumerable.Range(1, many).Select(i => $"doc-{i:D5}"), matches.Select(m => m.Id));
    }

    [Theory]
    [InlineData("kiwi")]
    [InlineData("omega")]
    public async Task AQueryWithinTheLimit_IsAnsweredAsWithoutOne(string query)
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);

        var without = await index.SearchMatchesAsync(query, Options(rankLimit: null));
        var with = await index.SearchMatchesAsync(query, Options(rankLimit: Documents));

        Assert.NotEmpty(without);
        Assert.Equal(without.Select(m => (m.Id, m.Score, m.Snippet)), with.Select(m => (m.Id, m.Score, m.Snippet)));
    }

    [Fact]
    public async Task ScoreMatches_ScoresAndSnippetsThePickedDocuments_BestFirst()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);
        var picked = new[] { "doc-0007", "doc-0100", "doc-0006", "doc-0007", "missing" };

        var scored = await index.ScoreMatchesAsync("omega", picked, Options(rankLimit: 200));

        Assert.Equal(new[] { "doc-0006", "doc-0007", "doc-0100" }, scored.Select(m => m.Id).Order());
        Assert.All(scored, m =>
        {
            Assert.InRange(m.Score, 0.5, 1.0);
            Assert.Contains("omega", m.Snippet!, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(scored.OrderByDescending(m => m.Score).ThenBy(m => m.Id, StringComparer.Ordinal).Select(m => m.Id), scored.Select(m => m.Id));
        Assert.Empty(await index.ScoreMatchesAsync("omega", Array.Empty<string>()));
        Assert.Empty(await index.ScoreMatchesAsync(" ", picked));
    }

    [Fact]
    public async Task ScoreMatches_DropsWhatIsUnderMinScore()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db);
        var options = Options(rankLimit: null);
        options.MinScore = 0.99;

        // "kiwi" is in doc-0100 only: the exact-match boost lifts it, the other text does not hold the word.
        var scored = await index.ScoreMatchesAsync("kiwi", new[] { "doc-0100", "doc-0101" }, options);

        Assert.DoesNotContain(scored, m => m.Id == "doc-0101");
    }
}
