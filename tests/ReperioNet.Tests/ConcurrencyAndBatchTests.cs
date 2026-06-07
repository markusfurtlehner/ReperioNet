using Xunit;

namespace ReperioNet.Tests;

public class ConcurrencyAndBatchTests
{
    [Fact]
    public async Task ParallelAddsAndSearches_NoBusyErrors_NoCorruption()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        const int writerCount = 100;
        const int searcherCount = 50;

        var writes = Enumerable.Range(0, writerCount)
            .Select(i => index.AddAsync(TestOptions.Entry($"doc-{i}", $"shared token{i} content")));
        var reads = Enumerable.Range(0, searcherCount)
            .Select(_ => index.SearchAsync("shared"));

        // Interleave writes and reads; the single-writer gate + WAL must absorb all contention.
        await Task.WhenAll(writes.Concat<Task>(reads));

        Assert.Equal(writerCount, await index.CountAsync());
        Assert.Equal(writerCount, (await index.SearchAsync("shared", new SearchQueryOptions { Limit = 1000 })).Count);

        // Index integrity: every document has exactly one FTS and one trigram row.
        Assert.Equal(writerCount, db.QueryScalarLong("SELECT COUNT(*) FROM documents_fts;"));
        Assert.Equal(writerCount, db.QueryScalarLong("SELECT COUNT(*) FROM documents_trgm;"));
    }

    [Fact]
    public async Task AddRange_TenThousandDocuments_SingleTransactionPerfSanity()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        var entries = Enumerable.Range(0, 10_000)
            .Select(i => TestOptions.Entry($"doc-{i}", $"document number {i} with token{i}"));

        await index.AddRangeAsync(entries);

        Assert.Equal(10_000, await index.CountAsync());
        Assert.Single(await index.SearchAsync("token9999"));
        Assert.Equal(10_000, db.QueryScalarLong("SELECT COUNT(*) FROM documents_trgm;"));
    }

    [Fact]
    public async Task CancelledAddRange_RollsBack()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db);

        using var cts = new CancellationTokenSource();

        IEnumerable<SearchEntry<TestMeta>> Entries()
        {
            for (var i = 0; i < 1000; i++)
            {
                if (i == 50)
                {
                    cts.Cancel();
                }

                yield return TestOptions.Entry($"doc-{i}", $"content {i}");
            }
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => index.AddRangeAsync(Entries(), cts.Token));

        // The single batch transaction must have been rolled back entirely.
        Assert.Equal(0, await index.CountAsync());
    }
}
