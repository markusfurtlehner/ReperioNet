using Xunit;

namespace ReperioNet.Tests;

public class LayoutMismatchTests
{
    private static Action<ReperioOptions<TestMeta>> Configure(Action<ReperioOptions<TestMeta>>? extra = null)
        => o =>
        {
            o.MetadataTypeInfo = TestMetaJsonContext.Default.TestMeta;
            extra?.Invoke(o);
        };

    /// <summary>Flips the option corresponding to a persisted layout-flag key away from its default.</summary>
    private static void FlipFlag(ReperioOptions<TestMeta> options, string flag)
    {
        switch (flag)
        {
            case "store_content":
                options.StoreContent = !options.StoreContent;
                break;
            case "enable_trigram":
                options.EnableTrigram = !options.EnableTrigram;
                break;
            case "enable_stemming":
                options.EnableStemming = !options.EnableStemming;
                break;
            case "enable_phonetic":
                options.EnablePhonetic = !options.EnablePhonetic;
                break;
            case "remove_stop_words":
                options.RemoveStopWords = !options.RemoveStopWords;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown layout flag.");
        }
    }

    [Theory]
    [InlineData("store_content")]
    [InlineData("enable_trigram")]
    [InlineData("enable_stemming")]
    [InlineData("enable_phonetic")]
    [InlineData("remove_stop_words")]
    public async Task Reopen_WithMismatchedLayoutFlag_ThrowsNamingTheFlag(string flag)
    {
        using var db = new TestDatabase();

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()))
        {
        }

        var ex = await Assert.ThrowsAsync<ReperioException>(
            () => SearchIndex<TestMeta>.OpenAsync(db.Path, Configure(o => FlipFlag(o, flag))));

        Assert.Contains(flag, ex.Message);
        Assert.Contains("RebuildAsync", ex.Message);
    }

    [Fact]
    public async Task Reopen_WithTamperedTokenizer_Throws()
    {
        using var db = new TestDatabase();

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()))
        {
        }

        db.ExecuteNonQuery("UPDATE reperio_meta SET value = 'ascii' WHERE key = 'tokenizer';");

        var ex = await Assert.ThrowsAsync<ReperioException>(
            () => SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()));

        Assert.Contains("tokenizer", ex.Message);
    }

    [Fact]
    public async Task Reopen_WithUnsupportedSchemaVersion_Throws()
    {
        using var db = new TestDatabase();

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()))
        {
        }

        db.ExecuteNonQuery("UPDATE reperio_meta SET value = '999' WHERE key = 'schema_version';");

        var ex = await Assert.ThrowsAsync<ReperioException>(
            () => SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()));

        Assert.Contains("schema_version", ex.Message);
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public async Task Reopen_WithMissingLayoutFlag_Throws()
    {
        using var db = new TestDatabase();

        await using (await SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()))
        {
        }

        db.ExecuteNonQuery("DELETE FROM reperio_meta WHERE key = 'enable_trigram';");

        var ex = await Assert.ThrowsAsync<ReperioException>(
            () => SearchIndex<TestMeta>.OpenAsync(db.Path, Configure()));

        Assert.Contains("enable_trigram", ex.Message);
    }
}
