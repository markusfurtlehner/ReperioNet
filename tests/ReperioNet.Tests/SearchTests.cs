using Xunit;

namespace ReperioNet.Tests;

public class SearchTests
{
    [Fact]
    public async Task Search_FindsDocumentByToken()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen"));

        var hits = await index.SearchAsync("rechnung");

        Assert.Single(hits);
        Assert.Equal("doc", hits[0].Id);

        // Sole candidate: normBm25 = 1.0 and the substring boost applies, so the §9.11 blend
        // guarantees at least 0.4 * 1.0 + 0.15; the fuzzy component (case-sensitive
        // Fuzz.TokenSetRatio per §9.10) adds the rest.
        Assert.InRange(hits[0].Score, 0.55, 1.0);
        Assert.Null(hits[0].Snippet);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task Search_NullOrWhitespaceQuery_ReturnsEmpty(string? query)
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "some content"));

        Assert.Empty(await index.SearchAsync(query!));
    }

    [Fact]
    public async Task Search_QueryWithoutAlphanumericTokens_ReturnsEmpty()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "some content"));

        Assert.Empty(await index.SearchAsync("!?! ... ---"));
    }

    [Fact]
    public async Task Search_MultipleTokens_OrSemantics()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("a", "apfel"),
            TestOptions.Entry("b", "birne"),
            TestOptions.Entry("c", "citrone"),
        ]);

        var hits = await index.SearchAsync("apfel birne");

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Id == "a");
        Assert.Contains(hits, h => h.Id == "b");
    }

    [Fact]
    public async Task Search_Bm25Ordering_MoreRelevantDocFirst()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("relevant", "kafka kafka kafka kafka"),
            TestOptions.Entry("diluted", "kafka and a lot of words diluting the term frequency considerably here"),
        ]);

        var hits = await index.SearchAsync("kafka");

        Assert.Equal(2, hits.Count);
        Assert.Equal("relevant", hits[0].Id);
        Assert.Equal("diluted", hits[1].Id);

        // §9.11 blend: the best candidate saturates at 1.0; the weaker one stays strictly between.
        Assert.Equal(1.0, hits[0].Score, 9);
        Assert.InRange(hits[1].Score, 0.000000001, 0.999999999);
    }

    [Fact]
    public async Task Search_ScoresDescendingAndWithinUnitRange()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(Enumerable.Range(1, 6).Select(i =>
            TestOptions.Entry($"doc-{i}", string.Join(' ', Enumerable.Repeat("zeta", i)) + " filler words here")));

        var hits = await index.SearchAsync("zeta");

        Assert.Equal(6, hits.Count);
        for (var i = 1; i < hits.Count; i++)
        {
            Assert.True(hits[i - 1].Score >= hits[i].Score, "scores must be non-increasing");
        }

        Assert.All(hits, h => Assert.InRange(h.Score, 0.0, 1.0));
    }

    [Fact]
    public async Task Search_IsCaseInsensitive_AndFoldsDiacritics()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Herr MÜLLER schreibt"));

        // unicode61 remove_diacritics 2 folds case and diacritics on both index and query side.
        Assert.Single(await index.SearchAsync("müller"));
        Assert.Single(await index.SearchAsync("muller"));
        Assert.Single(await index.SearchAsync("MULLER"));
        Assert.Single(await index.SearchAsync("Müller"));
    }

    [Theory]
    [InlineData("\"rechnung\"")]
    [InlineData("rechnung*")]
    [InlineData("(rechnung)")]
    [InlineData("rechnung:")]
    [InlineData("NEAR(rechnung)")]
    [InlineData("rechnung AND NOT x OR y\"")]
    public async Task Search_SpecialCharactersAndOperators_AreNeutralized(string query)
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist da"));

        // Must not throw, and the literal token "rechnung" inside the query still matches.
        var hits = await index.SearchAsync(query);

        Assert.Contains(hits, h => h.Id == "doc");
    }

    [Fact]
    public async Task Search_OperatorWords_AreTreatedAsLiteralTokens()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("a", "apfel"),
            TestOptions.Entry("or-doc", "containing the word or somewhere"),
        ]);

        // "OR" is tokenized/lowercased to the literal token "or" — never an FTS operator.
        var hits = await index.SearchAsync("apfel OR");

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task Search_LimitAndOffset_PageThroughResults()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        // Distinct term frequencies give a deterministic bm25 order: doc-5 best ... doc-1 worst.
        // Fuzzy is disabled and the second query token ("gamma") occurs nowhere, so no exact-match
        // boost applies and scores are pure normalized bm25 — strictly distinct, no tie-cap at 1.0.
        await index.AddRangeAsync(Enumerable.Range(1, 5).Select(i =>
            TestOptions.Entry($"doc-{i}", string.Join(' ', Enumerable.Repeat("omega", i)) + " padding text")));

        var pageOne = await index.SearchAsync("omega gamma", new SearchQueryOptions { Limit = 2, EnableFuzzy = false });
        var pageTwo = await index.SearchAsync("omega gamma", new SearchQueryOptions { Limit = 2, Offset = 2, EnableFuzzy = false });
        var beyond = await index.SearchAsync("omega gamma", new SearchQueryOptions { Limit = 2, Offset = 10, EnableFuzzy = false });

        Assert.Equal(new[] { "doc-5", "doc-4" }, pageOne.Select(h => h.Id));
        Assert.Equal(new[] { "doc-3", "doc-2" }, pageTwo.Select(h => h.Id));
        Assert.Empty(beyond);
    }

    [Fact]
    public async Task Search_StoreContentFalse_StillWorks_SnippetNull()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.StoreContent = false);

        await index.AddAsync(TestOptions.Entry("doc", "findable content"));

        var hits = await index.SearchAsync("findable", new SearchQueryOptions { IncludeSnippet = true });

        Assert.Single(hits);
        Assert.Null(hits[0].Snippet);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmpty()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "some content"));

        Assert.Empty(await index.SearchAsync("nonexistent"));
    }

    [Fact]
    public async Task Search_OnEmptyIndex_ReturnsEmpty()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        Assert.Empty(await index.SearchAsync("anything"));
    }
}
