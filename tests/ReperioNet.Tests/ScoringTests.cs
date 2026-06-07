using ReperioNet.Abstractions;
using Xunit;

namespace ReperioNet.Tests;

public class ScoringTests
{
    private sealed class RecordingRanker : IFuzzyRanker
    {
        public List<(string Query, string Text)> Calls { get; } = [];

        public double Score(string query, string candidateText)
        {
            Calls.Add((query, candidateText));
            return 1.0;
        }
    }

    // Two docs both matching "alpha"+"beta"; the first has the higher term frequency and shorter
    // length, so it wins bm25 deterministically. Neither contains the contiguous phrase "alpha beta".
    private const string BestDoc = "alpha alpha alpha und beta beta beta";
    private const string WorstDocUnboosted = "alpha und beta viele weitere woerter die hier stehen um den text zu verlaengern";
    private const string WorstDocBoosted = "alpha beta und viele weitere woerter die hier stehen um den text zu verlaengern";

    [Fact]
    public async Task FuzzyReRank_PrefersTextCloserToTheQuery()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("closer", "alpha rechnung"),
            TestOptions.Entry("farther", "alpha unrelated something"),
        ]);

        // Both docs are recalled via "alpha"; the typo'd second term only fuzzy-matches "closer".
        var hits = await index.SearchAsync("alpha rechnng");

        Assert.Equal(2, hits.Count);
        Assert.Equal("closer", hits[0].Id);
        Assert.True(hits[0].Score > hits[1].Score);
    }

    [Fact]
    public async Task EnableFuzzyFalse_ScoreIsPureNormalizedBm25()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.EnableTrigram = false);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("best", BestDoc),
            TestOptions.Entry("worst", WorstDocUnboosted),
        ]);

        var hits = await index.SearchAsync("alpha beta", new SearchQueryOptions { EnableFuzzy = false });

        Assert.Equal(2, hits.Count);
        Assert.Equal("best", hits[0].Id);
        Assert.Equal(1.0, hits[0].Score, 9);
        Assert.Equal(0.0, hits[1].Score, 9);
    }

    [Fact]
    public async Task ExactMatchBoost_AddsPointFifteen()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.EnableTrigram = false);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("best", BestDoc),
            TestOptions.Entry("worst", WorstDocBoosted), // contains the contiguous "alpha beta"
        ]);

        var hits = await index.SearchAsync("alpha beta", new SearchQueryOptions { EnableFuzzy = false });

        Assert.Equal(2, hits.Count);
        Assert.Equal("best", hits[0].Id);
        Assert.Equal(1.0, hits[0].Score, 9);    // norm 1.0; boost capped at 1.0 anyway
        Assert.Equal(0.15, hits[1].Score, 9);   // norm 0.0 + 0.15 boost
    }

    [Fact]
    public async Task ExactMatchBoost_IsDiacriticAndCaseInsensitive()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.EnableTrigram = false);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("best", "kunde kunde kunde und MÜLLER und nochmal kunde"),
            TestOptions.Entry("worst", "kunde MÜLLER schreibt einen sehr langen brief an die versicherung wegen einer sache"),
        ]);

        // Folded query "kunde muller" matches the folded contiguous "kunde MÜLLER" in "worst" only.
        var hits = await index.SearchAsync("Kunde Müller", new SearchQueryOptions { EnableFuzzy = false });

        Assert.Equal(2, hits.Count);
        Assert.Equal("best", hits[0].Id);
        Assert.Equal(0.15, hits[1].Score, 9);
    }

    [Fact]
    public async Task MinScore_DropsLowScoringHits()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.EnableTrigram = false);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("best", BestDoc),
            TestOptions.Entry("worst", WorstDocUnboosted),
        ]);

        var hits = await index.SearchAsync(
            "alpha beta",
            new SearchQueryOptions { EnableFuzzy = false, MinScore = 0.5 });

        Assert.Single(hits);
        Assert.Equal("best", hits[0].Id);
    }

    [Fact]
    public async Task MinScore_BoostCanLiftAHitAboveTheThreshold()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.EnableTrigram = false);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("best", BestDoc),
            TestOptions.Entry("worst", WorstDocBoosted),
        ]);

        var hits = await index.SearchAsync(
            "alpha beta",
            new SearchQueryOptions { EnableFuzzy = false, MinScore = 0.1 });

        // Without the boost the worst doc scores 0.0 and would be dropped; the boost lifts it to 0.15.
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task FuzzyRanker_ReceivesStoredContent()
    {
        using var db = new TestDatabase();
        var ranker = new RecordingRanker();
        await using var index = await TestOptions.OpenAsync(db, o => o.FuzzyRanker = ranker);

        await index.AddAsync(TestOptions.Entry("doc", "fuzzy source text"));
        await index.SearchAsync("fuzzy");

        var call = Assert.Single(ranker.Calls);
        Assert.Equal("fuzzy", call.Query);
        Assert.Equal("fuzzy source text", call.Text);
    }

    [Fact]
    public async Task FuzzyRanker_ReceivesRankTextWhenContentNotStored()
    {
        using var db = new TestDatabase();
        var ranker = new RecordingRanker();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.StoreContent = false;
            o.FuzzyRanker = ranker;
        });

        await index.AddAsync(TestOptions.Entry("doc", "fuzzy source text"));
        await index.SearchAsync("fuzzy");

        // §9.10: text = content ?? rank_text — with StoreContent=false, rank_text carries the text.
        var call = Assert.Single(ranker.Calls);
        Assert.Equal("fuzzy source text", call.Text);
    }

    [Fact]
    public async Task EnableFuzzyFalse_RankerIsNotCalled()
    {
        using var db = new TestDatabase();
        var ranker = new RecordingRanker();
        await using var index = await TestOptions.OpenAsync(db, o => o.FuzzyRanker = ranker);

        await index.AddAsync(TestOptions.Entry("doc", "some content"));
        await index.SearchAsync("content", new SearchQueryOptions { EnableFuzzy = false });

        Assert.Empty(ranker.Calls);
    }

    [Fact]
    public async Task Scores_NeverExceedOne()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        // Exact content match: fuzzy 1.0, normBm25 1.0, plus boost — must cap at 1.0.
        await index.AddAsync(TestOptions.Entry("doc", "exakte rechnung"));

        var hits = await index.SearchAsync("exakte rechnung");

        Assert.Single(hits);
        Assert.Equal(1.0, hits[0].Score, 9);
    }
}
