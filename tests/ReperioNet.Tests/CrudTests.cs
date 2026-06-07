using ReperioNet.Abstractions;
using Xunit;

namespace ReperioNet.Tests;

public class CrudTests
{
    private sealed class FixedDetector(string? result) : ILanguageDetector
    {
        public string? Detect(string text) => result;
    }

    [Fact]
    public async Task Add_ThenContainsAndCount()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("a", "hello world"));

        Assert.True(await index.ContainsAsync("a"));
        Assert.False(await index.ContainsAsync("b"));
        Assert.Equal(1, await index.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Add_NullOrEmptyId_ThrowsArgumentException(string? id)
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        var entry = new SearchEntry<TestMeta>(id!, "content", new TestMeta("n", 1));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => index.AddAsync(entry));
        Assert.Equal(0, await index.CountAsync());
    }

    [Fact]
    public async Task Add_NullEntry_ThrowsArgumentNullException()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await Assert.ThrowsAsync<ArgumentNullException>(() => index.AddAsync(null!));
    }

    [Fact]
    public async Task Upsert_SameId_CountStaysOne_RowidStable()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "alpha original"));
        var rowidBefore = db.QueryScalarLong("SELECT rowid FROM documents WHERE doc_id = 'doc';");

        await index.AddAsync(TestOptions.Entry("doc", "alpha updated"));
        var rowidAfter = db.QueryScalarLong("SELECT rowid FROM documents WHERE doc_id = 'doc';");

        Assert.Equal(1, await index.CountAsync());
        Assert.Equal(rowidBefore, rowidAfter);

        // FTS rows must not accumulate: exactly one per table for the document.
        Assert.Equal(1, db.QueryScalarLong("SELECT COUNT(*) FROM documents_fts;"));
        Assert.Equal(1, db.QueryScalarLong("SELECT COUNT(*) FROM documents_trgm;"));
    }

    [Fact]
    public async Task Upsert_ReplacesIndexedContent_NoDuplicateHits()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "alpha original"));
        await index.AddAsync(TestOptions.Entry("doc", "alpha updated"));

        var alphaHits = await index.SearchAsync("alpha");
        Assert.Single(alphaHits);
        Assert.Equal("doc", alphaHits[0].Id);

        Assert.Empty(await index.SearchAsync("original"));
        Assert.Single(await index.SearchAsync("updated"));
    }

    [Fact]
    public async Task Remove_Existing_ReturnsTrue_AndRemovesAllRows()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "hello world"));

        Assert.True(await index.RemoveAsync("doc"));
        Assert.False(await index.ContainsAsync("doc"));
        Assert.Equal(0, await index.CountAsync());
        Assert.Empty(await index.SearchAsync("hello"));
        Assert.Equal(0, db.QueryScalarLong("SELECT COUNT(*) FROM documents_fts;"));
        Assert.Equal(0, db.QueryScalarLong("SELECT COUNT(*) FROM documents_trgm;"));
    }

    [Fact]
    public async Task Remove_Unknown_ReturnsFalse()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        Assert.False(await index.RemoveAsync("missing"));
    }

    [Fact]
    public async Task Clear_RemovesEverything()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(Enumerable.Range(0, 10).Select(i => TestOptions.Entry($"doc-{i}", $"content {i}")));
        await index.ClearAsync();

        Assert.Equal(0, await index.CountAsync());
        Assert.Empty(await index.SearchAsync("content"));
        Assert.Equal(0, db.QueryScalarLong("SELECT COUNT(*) FROM documents_fts;"));
        Assert.Equal(0, db.QueryScalarLong("SELECT COUNT(*) FROM documents_trgm;"));
    }

    [Fact]
    public async Task AddRange_InsertsAll()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(Enumerable.Range(0, 100).Select(i => TestOptions.Entry($"doc-{i}", $"token{i} shared")));

        Assert.Equal(100, await index.CountAsync());
        Assert.Single(await index.SearchAsync("token42"));
    }

    [Fact]
    public async Task AddRange_InvalidEntry_RollsBackWholeBatch()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        var entries = new[]
        {
            TestOptions.Entry("ok-1", "first"),
            new SearchEntry<TestMeta>("", "bad", new TestMeta("n", 1)),
            TestOptions.Entry("ok-2", "second"),
        };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => index.AddRangeAsync(entries));

        // One transaction per batch: nothing from the failed batch may persist.
        Assert.Equal(0, await index.CountAsync());
    }

    [Fact]
    public async Task AddRange_DuplicateIdWithinBatch_LastWins()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(
        [
            TestOptions.Entry("doc", "first version"),
            TestOptions.Entry("doc", "second version"),
        ]);

        Assert.Equal(1, await index.CountAsync());
        Assert.Empty(await index.SearchAsync("first"));
        Assert.Single(await index.SearchAsync("second"));
    }

    [Fact]
    public async Task Metadata_RoundTripsThroughJson()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        var meta = new TestMeta("Ärger \"quoted\" \\ path", 42);
        await index.AddAsync(new SearchEntry<TestMeta>("doc", "searchable text", meta));

        var hits = await index.SearchAsync("searchable");
        Assert.Single(hits);
        Assert.Equal(meta, hits[0].Metadata);
    }

    [Fact]
    public async Task Language_ExplicitCode_IsPersisted()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "text", language: "de"));

        Assert.Equal("de", db.QueryScalar("SELECT language FROM documents WHERE doc_id = 'doc';"));
    }

    [Fact]
    public async Task Language_NoSource_IsNull()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "text"));

        Assert.Null(db.QueryScalar("SELECT language FROM documents WHERE doc_id = 'doc';"));
    }

    [Fact]
    public async Task Language_ResolutionOrder_EntryThenDetectorThenDefault()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.LanguageDetector = new FixedDetector("fr");
            o.DefaultLanguage = "en";
        });

        await index.AddAsync(TestOptions.Entry("explicit", "text", language: "de"));
        await index.AddAsync(TestOptions.Entry("detected", "text"));

        Assert.Equal("de", db.QueryScalar("SELECT language FROM documents WHERE doc_id = 'explicit';"));
        Assert.Equal("fr", db.QueryScalar("SELECT language FROM documents WHERE doc_id = 'detected';"));
    }

    [Fact]
    public async Task Language_DetectorUncertain_FallsBackToDefault()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.LanguageDetector = new FixedDetector(null);
            o.DefaultLanguage = "en";
        });

        await index.AddAsync(TestOptions.Entry("doc", "text"));

        Assert.Equal("en", db.QueryScalar("SELECT language FROM documents WHERE doc_id = 'doc';"));
    }

    [Fact]
    public async Task ColumnLayout_StoreContentTrue_ContentSet_RankTextEmpty()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", "the original text"));

        Assert.Equal("the original text", db.QueryScalar("SELECT content FROM documents WHERE doc_id = 'doc';"));
        Assert.Equal("", db.QueryScalar("SELECT rank_text FROM documents WHERE doc_id = 'doc';"));
    }

    [Fact]
    public async Task ColumnLayout_StoreContentFalse_ContentNull_RankTextSet()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.StoreContent = false);

        await index.AddAsync(TestOptions.Entry("doc", "the original text"));

        Assert.Null(db.QueryScalar("SELECT content FROM documents WHERE doc_id = 'doc';"));
        Assert.Equal("the original text", db.QueryScalar("SELECT rank_text FROM documents WHERE doc_id = 'doc';"));
    }

    [Fact]
    public async Task MaxContentChars_TruncatesIndexedAndStoredText()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.MaxContentChars = 10);

        // Truncating to 10 chars keeps exactly "alpha beta"; "GAMMA" falls outside the cap.
        await index.AddAsync(TestOptions.Entry("doc", "alpha beta GAMMA"));

        Assert.Equal("alpha beta", db.QueryScalar("SELECT content FROM documents WHERE doc_id = 'doc';"));
        Assert.Single(await index.SearchAsync("alpha"));
        Assert.Empty(await index.SearchAsync("gamma"));
    }

    [Fact]
    public async Task TrigramRows_AreWrittenWhenEnabled()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddRangeAsync(Enumerable.Range(0, 5).Select(i => TestOptions.Entry($"doc-{i}", $"content {i}")));

        Assert.Equal(5, db.QueryScalarLong("SELECT COUNT(*) FROM documents_trgm;"));
    }

    [Fact]
    public async Task TrigramDisabled_WritesNoTrigramRows()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.EnableTrigram = false);

        await index.AddAsync(TestOptions.Entry("doc", "content"));

        Assert.False(db.TableExists("documents_trgm"));
        Assert.Single(await index.SearchAsync("content"));
    }

    [Fact]
    public async Task EmptyContent_IsAllowed()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        await index.AddAsync(TestOptions.Entry("doc", ""));

        Assert.True(await index.ContainsAsync("doc"));
        Assert.Equal(1, await index.CountAsync());
    }
}
