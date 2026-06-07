using Xunit;

namespace ReperioNet.Tests;

/// <summary>Integration tests for snippet generation through SearchAsync (§9.13).</summary>
public class SnippetTests
{
    [Fact]
    public async Task Snippet_MarksMatchedToken_PreservingOriginalCasing()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist im Anhang"));

        var hits = await index.SearchAsync("rechnung", new SearchQueryOptions { IncludeSnippet = true });

        Assert.Single(hits);
        Assert.Equal("Die <mark>Rechnung</mark> ist im Anhang", hits[0].Snippet);
    }

    [Fact]
    public async Task Snippet_MarkingIsDiacriticAndCaseInsensitive()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Herr MÜLLER kommt morgen"));

        var hits = await index.SearchAsync("muller", new SearchQueryOptions { IncludeSnippet = true });

        Assert.Single(hits);
        Assert.Equal("Herr <mark>MÜLLER</mark> kommt morgen", hits[0].Snippet);
    }

    [Fact]
    public async Task Snippet_WindowIsCenteredOnTheFirstMatch()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        var content = new string('a', 150) + " kafka " + new string('b', 150);
        await index.AddAsync(TestOptions.Entry("doc", content));

        var hits = await index.SearchAsync("kafka", new SearchQueryOptions
        {
            IncludeSnippet = true,
            Snippet = { MaxLength = 21 },
        });

        Assert.Single(hits);
        Assert.Equal("aaaaaaa <mark>kafka</mark> bbbbbbb", hits[0].Snippet);
    }

    [Fact]
    public async Task Snippet_MarksEveryOccurrenceInTheWindow()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "kafka liest kafka"));

        var hits = await index.SearchAsync("kafka", new SearchQueryOptions { IncludeSnippet = true });

        Assert.Single(hits);
        Assert.Equal("<mark>kafka</mark> liest <mark>kafka</mark>", hits[0].Snippet);
    }

    [Fact]
    public async Task Snippet_SubstringQueryToken_IsMarkedInsideAWord()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist im Anhang"));

        // Trigram-recalled substring queries mark the matched fragment inside the word.
        var hits = await index.SearchAsync("chnun", new SearchQueryOptions { IncludeSnippet = true });

        Assert.Single(hits);
        Assert.Equal("Die Re<mark>chnun</mark>g ist im Anhang", hits[0].Snippet);
    }

    [Fact]
    public async Task Snippet_CustomMarkers()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist im Anhang"));

        var hits = await index.SearchAsync("rechnung", new SearchQueryOptions
        {
            IncludeSnippet = true,
            Snippet = { StartMarker = "[", EndMarker = "]" },
        });

        Assert.Single(hits);
        Assert.Equal("Die [Rechnung] ist im Anhang", hits[0].Snippet);
    }

    [Fact]
    public async Task Snippet_NotRequested_IsNull()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist im Anhang"));

        var hits = await index.SearchAsync("rechnung");

        Assert.Single(hits);
        Assert.Null(hits[0].Snippet);
    }
}
