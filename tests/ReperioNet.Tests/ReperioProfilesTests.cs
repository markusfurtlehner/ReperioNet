using Xunit;

namespace ReperioNet.Tests;

public class ReperioProfilesTests
{
    [Fact]
    public async Task UseDesktopProfile_SetsExactlyTheDocumentedFlags()
    {
        using var db = new TestDatabase();
        ReperioOptions<TestMeta>? seen = null;

        await using (await TestOptions.OpenAsync(db, o => seen = o.UseDesktopProfile()))
        {
        }

        Assert.NotNull(seen);
        Assert.True(seen.EnableTrigram);
        Assert.True(seen.StoreContent);
        Assert.True(seen.EnablePhonetic);
        Assert.False(seen.RemoveStopWords);
        Assert.Equal(0, seen.MaxContentChars);
    }

    [Fact]
    public async Task UseMobileProfile_SetsExactlyTheDocumentedFlags()
    {
        using var db = new TestDatabase();
        ReperioOptions<TestMeta>? seen = null;

        await using (await TestOptions.OpenAsync(db, o => seen = o.UseMobileProfile()))
        {
        }

        Assert.NotNull(seen);
        Assert.False(seen.EnableTrigram);
        Assert.True(seen.StoreContent);
        Assert.True(seen.EnablePhonetic);
        Assert.True(seen.RemoveStopWords);
        Assert.Equal(4000, seen.MaxContentChars);
    }

    [Fact]
    public async Task Profiles_ReturnOptionsForChaining()
    {
        using var db = new TestDatabase();
        var chained = false;

        await using (await TestOptions.OpenAsync(db, o => chained = ReferenceEquals(o.UseMobileProfile(), o)))
        {
        }

        Assert.True(chained);
    }

    [Fact]
    public async Task MobileProfile_IndexesAndSearches()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.UseMobileProfile());

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung ist angekommen"));

        Assert.Single(await index.SearchAsync("rechnung"));

        // Trigram is off: mid-word substring queries do not match under the mobile profile.
        Assert.Empty(await index.SearchAsync("chnun"));
    }

    [Fact]
    public async Task SwitchingProfiles_OnExistingDatabase_ThrowsLayoutMismatch()
    {
        using var db = new TestDatabase();

        await using (await TestOptions.OpenAsync(db, o => o.UseDesktopProfile()))
        {
        }

        // The mobile profile changes persisted layout flags -> ReperioException with the
        // documented RebuildAsync guidance, never a silent rebuild.
        var ex = await Assert.ThrowsAsync<ReperioException>(
            () => TestOptions.OpenAsync(db, o => o.UseMobileProfile()));

        Assert.Contains("RebuildAsync", ex.Message);
    }

    [Fact]
    public async Task MobileProfile_CapsIndexedContentAtFourThousandChars()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.UseMobileProfile());

        var longBody = new string('a', 4000) + " hinterland";
        await index.AddAsync(TestOptions.Entry("doc", longBody));

        // "hinterland" lies beyond the 4000-char cap and must not be indexed.
        Assert.Empty(await index.SearchAsync("hinterland"));
        Assert.Equal(4000L, db.QueryScalarLong("SELECT length(content) FROM documents WHERE doc_id = 'doc';"));
    }
}
