using Microsoft.Data.Sqlite;
using ReperioNet.Internal;
using Xunit;

namespace ReperioNet.Tests;

public class OpenAsyncTests
{
    private static Action<ReperioOptions<TestMeta>> Configure(Action<ReperioOptions<TestMeta>>? extra = null)
        => o =>
        {
            o.MetadataTypeInfo = TestMetaJsonContext.Default.TestMeta;
            extra?.Invoke(o);
        };

    [Fact]
    public async Task OpenAsync_CreatesFreshDatabase()
    {
        using var db = new TestDatabase();

        await using var index = await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure());

        Assert.NotNull(index);
        Assert.True(File.Exists(db.Path));
    }

    [Fact]
    public async Task OpenAsync_CreatesExpectedTables()
    {
        using var db = new TestDatabase();

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()))
        {
        }

        Assert.True(db.TableExists("reperio_meta"));
        Assert.True(db.TableExists("documents"));
        Assert.True(db.TableExists("documents_fts"));
        Assert.True(db.TableExists("documents_trgm"));
    }

    [Fact]
    public async Task OpenAsync_WithTrigramDisabled_DoesNotCreateTrigramTable()
    {
        using var db = new TestDatabase();

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure(o => o.EnableTrigram = false)))
        {
        }

        Assert.True(db.TableExists("documents_fts"));
        Assert.False(db.TableExists("documents_trgm"));
        Assert.Equal("false", db.ReadMeta()["enable_trigram"]);
    }

    [Fact]
    public async Task OpenAsync_WritesSchemaVersionAndLayoutFlags()
    {
        using var db = new TestDatabase();

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()))
        {
        }

        var meta = db.ReadMeta();
        Assert.Equal("1", meta["schema_version"]);
        Assert.Equal("true", meta["store_content"]);
        Assert.Equal("true", meta["enable_trigram"]);
        Assert.Equal("true", meta["enable_stemming"]);
        Assert.Equal("true", meta["enable_phonetic"]);
        Assert.Equal("false", meta["remove_stop_words"]);
        Assert.Equal("unicode61 remove_diacritics 2", meta["tokenizer"]);
        Assert.Equal(7, meta.Count);
    }

    [Fact]
    public async Task OpenAsync_PersistsNonDefaultFlags()
    {
        using var db = new TestDatabase();

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure(o =>
        {
            o.StoreContent = false;
            o.EnableStemming = false;
            o.RemoveStopWords = true;
        })))
        {
        }

        var meta = db.ReadMeta();
        Assert.Equal("false", meta["store_content"]);
        Assert.Equal("false", meta["enable_stemming"]);
        Assert.Equal("true", meta["remove_stop_words"]);
    }

    [Fact]
    public async Task OpenAsync_ReopensExistingDatabase()
    {
        using var db = new TestDatabase();

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()))
        {
        }

        await using var reopened = await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure());

        Assert.NotNull(reopened);

        // Reopening must not duplicate or rewrite meta rows.
        Assert.Equal(7, db.ReadMeta().Count);
    }

    [Fact]
    public async Task OpenAsync_ReopensWithMatchingNonDefaultOptions()
    {
        using var db = new TestDatabase();
        Action<ReperioOptions<TestMeta>> nonDefault = o =>
        {
            o.EnableTrigram = false;
            o.RemoveStopWords = true;
        };

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure(nonDefault)))
        {
        }

        await using var reopened = await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure(nonDefault));

        Assert.NotNull(reopened);
    }

    [Fact]
    public async Task OpenAsync_NullMetadataTypeInfo_ThrowsReperioException()
    {
        using var db = new TestDatabase();

        var ex = await Assert.ThrowsAsync<ReperioException>(
            () => SearchIndex<TestMeta>.OpenAsync(db.Path));

        Assert.Contains("MetadataTypeInfo", ex.Message);
        Assert.Contains("JsonTypeInfo", ex.Message);
    }

    [Fact]
    public async Task OpenAsync_FtsAvailabilityCheck_PassesOnBundledEngine()
    {
        // OpenAsync performs the FTS5 + version checks internally; succeeding on the bundled
        // e_sqlite3 engine proves both pass.
        using var db = new TestDatabase();

        await using var index = await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure());

        Assert.NotNull(index);
    }

    [Fact]
    public void BundledEngine_SupportsFts5Directly()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE VIRTUAL TABLE temp.__fts5probe USING fts5(x);
            DROP TABLE temp.__fts5probe;
            """;
        var exception = Record.Exception(() => command.ExecuteNonQuery());

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesDatabaseFile()
    {
        using var db = new TestDatabase();

        var index = await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure());
        await index.DisposeAsync();

        // After a clean dispose (checkpoint + close + pool drain) the file must be deletable.
        File.Delete(db.Path);
        Assert.False(File.Exists(db.Path));
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        using var db = new TestDatabase();

        var index = await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure());
        await index.DisposeAsync();
        var exception = await Record.ExceptionAsync(async () => await index.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Methods_AfterDispose_ThrowObjectDisposed()
    {
        using var db = new TestDatabase();

        var index = await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure());
        await index.DisposeAsync();

        var entry = new SearchEntry<TestMeta>("id-1", "content", new TestMeta("n", 1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => index.AddAsync(entry));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => index.SearchAsync("query"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => index.CountAsync());
    }
}
