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

    [Theory]
    [InlineData(true, "om")]
    [InlineData(false, "om")]
    [InlineData(true, "ÄÖ")]
    [InlineData(false, "number 14")]
    [InlineData(true, "e")]
    [InlineData(true, "second LINE")]
    [InlineData(true, "nowhere")]
    public async Task AmongGivenDocuments_TheOnesThatContainAValue_AreTheOnesAPlainSubstringTestFinds(bool storeContent, string value)
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db, storeContent);

        foreach (var count in new[] { 1, 40, Documents })
        {
            var asked = Enumerable.Range(1, count).Select(i => i * (Documents / count)).ToList();
            var found = await index.FindContainingAmongAsync(value, asked.Select(i => new TextSlice($"doc-{i:D4}")).Append(new TextSlice("nope")).ToList());

            var want = asked.Where(i => TextOf(i).Contains(value, StringComparison.OrdinalIgnoreCase)).Select(i => $"doc-{i:D4}");
            Assert.Equal(want.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData(0, 5, "größe")]
    [InlineData(0, 4, "größe")]
    [InlineData(6, 3, "äöü")]
    [InlineData(7, 3, "äöü")]
    [InlineData(10, 100000, "second line")]
    [InlineData(100000, 5, "e")]
    [InlineData(3, 0, "")]
    public async Task ASlice_LimitsTheTestToThatPartOfTheText(int start, int length, string value)
    {
        using var db = new TestDatabase();
        await using var index = await SeedAsync(db, storeContent: true);

        var found = await index.FindContainingAmongAsync(value, Enumerable.Range(1, 300).Select(i => new TextSlice($"doc-{i:D4}", start, length)).ToList());

        var want = Enumerable.Range(1, 300).Where(i =>
        {
            var text = TextOf(i);
            var from = Math.Min(start, text.Length);
            return text.Substring(from, Math.Min(length, text.Length - from)).Contains(value, StringComparison.OrdinalIgnoreCase);
        }).Select(i => $"doc-{i:D4}");
        Assert.Equal(want.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestingManyLongTexts_DoesNotMakeAStringOrAnArrayOfEachText(bool storeContent)
    {
        // 600 texts of 40,000 characters: 24 MB as UTF-8, 48 MB as strings.
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o => o.StoreContent = storeContent);
        var filler = string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet ", 1480));
        await index.AddRangeAsync(Enumerable.Range(1, 600).Select(i => TestOptions.Entry($"doc-{i:D4}", filler + (i % 4 == 0 ? "zq" : "xx"))));
        var documents = Enumerable.Range(1, 600).Select(i => new TextSlice($"doc-{i:D4}")).ToList();
        await index.FindContainingAmongAsync("zq", documents);   // the first call makes the buffers

        // The count is of the whole process, and other tests run beside this one: the smallest of a few runs is this call's.
        var allocated = long.MaxValue;
        IReadOnlySet<string> found = new HashSet<string>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            found = await index.FindContainingAmongAsync("zq", documents);
            allocated = Math.Min(allocated, GC.GetTotalAllocatedBytes(precise: true) - before);
        }

        Assert.Equal(150, found.Count);
        Assert.True(allocated < 6_000_000, $"{allocated:N0} bytes were allocated to test 24 MB of text");
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
