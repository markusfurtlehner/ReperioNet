using Xunit;

namespace ReperioNet.Tests;

/// <summary>
/// 2.7: <c>GetTextsAsync</c> returns the texts the documents were added with, by id, whether or not the index stores content,
/// for a short list (one read) and for a long one (read in parts, side by side).
/// </summary>
public class GetTextsTests
{
    private const int Documents = 1500;

    private static string TextOf(int i) => $"Größe ÄÖÜ number {i}: " + string.Join(' ', Enumerable.Repeat("omega", 1 + (i % 7))) + "\n  second line";

    private static async Task<SearchIndex<TestMeta>> SeedAsync(TestDatabase db, bool storeContent)
    {
        var index = await TestOptions.OpenAsync(db, o => o.StoreContent = storeContent);
        await index.AddRangeAsync(Enumerable.Range(1, Documents).Select(i => TestOptions.Entry($"doc-{i:D4}", TextOf(i))));
        return index;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheTextsAreTheOnesTheDocumentsWereAddedWith(bool storeContent)
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db, storeContent);

        foreach (var count in new[] { 1, 3, 200, Documents })
        {
            var ids = Enumerable.Range(1, count).Select(i => $"doc-{i:D4}").ToList();
            var texts = await index.GetTextsAsync(ids);

            Assert.Equal(count, texts.Count);
            Assert.All(Enumerable.Range(1, count), i => Assert.Equal(TextOf(i), texts[$"doc-{i:D4}"]));
        }
    }

    [Fact]
    public async Task AnUnknownIdIsLeftOut_ARepeatedIdIsReadOnce_AndNoIdsGiveNothing()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db, storeContent: true);

        var texts = await index.GetTextsAsync(new[] { "doc-0007", "nope", "doc-0007", "doc-0009" });

        Assert.Equal(new[] { "doc-0007", "doc-0009" }, texts.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(await index.GetTextsAsync(Array.Empty<string>()));
    }

    [Theory]
    [InlineData("omega omega omega")]
    [InlineData("OMEGA")]
    [InlineData("meg")]
    [InlineData("größe äöü")]
    [InlineData("GRÖSSE")]
    [InlineData("number 14")]
    [InlineData("7: omega")]
    [InlineData("line")]
    [InlineData("\n  second")]
    [InlineData("not in any text")]
    [InlineData("say \"omega")]
    public async Task TheDocumentsThatContainAValue_AreTheOnesAPlainSubstringTestFinds(string value)
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db, storeContent: true);

        var found = await index.TryFindContainingAsync(value);

        var want = Enumerable.Range(1, Documents).Where(i => TextOf(i).Contains(value, StringComparison.OrdinalIgnoreCase)).Select(i => $"doc-{i:D4}");
        Assert.NotNull(found);
        Assert.Equal(want.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AValueOfLessThanThreeCharacters_OrAnIndexWithoutTrigrams_GivesNoAnswer()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db, storeContent: true);
        Assert.Null(await index.TryFindContainingAsync("om"));
        Assert.Null(await index.TryFindContainingAsync(""));
        Assert.NotNull(await index.TryFindContainingAsync("ome"));

        using var other = new TestDatabase();
        await using var plain = await TestOptions.OpenAsync(other, o => o.EnableTrigram = false);
        await plain.AddAsync(TestOptions.Entry("a", "omega"));
        Assert.Null(await plain.TryFindContainingAsync("omega"));
    }

    [Fact]
    public async Task AReplacedOrRemovedDocument_IsFoundByItsNewTextOnly()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db, storeContent: true);

        await index.AddAsync(TestOptions.Entry("doc-0001", "a new text with zeta"));
        await index.RemoveAsync("doc-0002");

        Assert.Equal(new[] { "doc-0001" }, await index.TryFindContainingAsync("zeta"));
        var omega = await index.TryFindContainingAsync("omega");
        Assert.Equal(Documents - 2, omega!.Count);
    }

    [Fact]
    public async Task AReplacedDocumentGivesItsNewText_AndARemovedOneIsGone()
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db, storeContent: true);

        await index.AddAsync(TestOptions.Entry("doc-0001", "a new text"));
        await index.RemoveAsync("doc-0002");
        var texts = await index.GetTextsAsync(new[] { "doc-0001", "doc-0002" });

        Assert.Equal("a new text", Assert.Single(texts).Value);
    }
}
