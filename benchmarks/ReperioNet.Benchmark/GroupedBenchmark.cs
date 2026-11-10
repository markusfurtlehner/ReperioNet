using System.Diagnostics;
using System.Text;

namespace ReperioNet.Benchmark;

/// <summary>
/// Grouped/ordered query benchmark (PRD §4a): N mail-like documents in G skewed groups (one key per
/// document, 20% with a second), list pages with a realistic filter (account in 2 of 3, role not
/// junk/trash) plus an unread count, thread pages, and the cost of incremental writes.
/// </summary>
public static class GroupedBenchmark
{
    private const string Conv = "conv";

    public static async Task<int> RunAsync(string dbPath, long docs, int groups, int iters, int batch, string label, string? mdBase)
    {
        Console.WriteLine($"ReperioNet grouped benchmark — label={label} docs={docs:N0} groups={groups:N0} iters={iters}");
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(dbPath + suffix);
        }

        var rng = new Random(42);
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        SearchEntry<EmailMeta> Make(long i)
        {
            var key = (int)(groups * Math.Pow(rng.NextDouble(), 2));
            var fields = new EntryFields();
            fields.Keys(Conv, rng.Next(5) == 0 ? ["c" + key, "c" + ((key + 1) % groups)] : ["c" + key]);
            fields.Keys("account", "a" + rng.Next(3));
            fields.Keys("role", rng.Next(10) switch { 0 => "junk", 1 => "trash", _ => "inbox" });
            fields.Number("date", baseDate.AddSeconds(rng.NextInt64(0, 5L * 365 * 86400)));
            fields.Flag("unread", rng.Next(5) == 0);
            return new SearchEntry<EmailMeta>("m" + i, "message " + i, new EmailMeta("s" + i, "f"), "en", fields);
        }

        await using var index = await SearchIndex<EmailMeta>.OpenAsync(dbPath, o =>
        {
            o.MetadataTypeInfo = BenchmarkJsonContext.Default.EmailMeta;
            o.EnableTrigram = false;
            o.StoreContent = false;
            o.EnablePhonetic = false;
            o.EnableStemming = false;
            o.Fields.Number("date").Flag("unread").Keys("account").Keys("role").Keys(Conv, orderBy: "date");
        });

        var indexing = Stopwatch.StartNew();
        for (long start = 0; start < docs; start += batch)
        {
            var count = (int)Math.Min(batch, docs - start);
            await index.AddRangeAsync(Enumerable.Range(0, count).Select(k => Make(start + k)).ToList());
        }

        await index.OptimizeAsync();
        var indexSeconds = indexing.Elapsed.TotalSeconds;
        Console.WriteLine($"indexed {docs:N0} docs in {indexSeconds:F1} s ({docs / indexSeconds:N0} docs/s)");

        var filter = FieldFilter.And(FieldFilter.KeyIn("account", "a0", "a1"), FieldFilter.KeyNotIn("role", "junk", "trash"));
        GroupQuery ListQuery(string? after, int size = 50)
        {
            var q = new GroupQuery { GroupField = Conv, Filter = filter, PageSize = size, After = after };
            q.CountWhere.Add("unread");
            return q;
        }

        // The cursor of page 200 (deep list) and the biggest thread.
        string? cursor = null;
        for (var p = 0; p < 200; p++)
        {
            var page = await index.GroupsAsync(ListQuery(cursor));
            cursor = page.Next;
            if (cursor is null)
            {
                break;
            }
        }

        var rows = new List<(string Name, int Items, double P50, double P95, double Max)>();
        async Task Measure(string name, Func<Task<int>> run)
        {
            await run(); // warm-up
            var times = new List<double>();
            var items = 0;
            for (var i = 0; i < iters; i++)
            {
                var sw = Stopwatch.StartNew();
                items = await run();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }

            times.Sort();
            rows.Add((name, items, times[times.Count / 2], times[(int)(times.Count * 0.95)], times[^1]));
            Console.WriteLine($"  {name,-46} items={items,4}  p50={rows[^1].P50,7:F2} ms  p95={rows[^1].P95,7:F2} ms  max={rows[^1].Max,7:F2} ms");
        }

        var firstPage = await index.GroupsAsync(ListQuery(null));
        Console.WriteLine($"  page 1 counts {firstPage.Groups.Sum(g => g.Count):N0} members (largest group {firstPage.Groups.Max(g => g.Count):N0})");
        await Measure("list page 1 (50 groups, filter, unread)", async () => (await index.GroupsAsync(ListQuery(null))).Groups.Count);
        await Measure("list page 200 (cursor, filter, unread)", async () => (await index.GroupsAsync(ListQuery(cursor))).Groups.Count);
        await Measure("list page 1, no filter, no counts", async () =>
            (await index.GroupsAsync(new GroupQuery { GroupField = Conv })).Groups.Count);
        await Measure("thread page 1 (typical group, filter)", async () =>
            (await index.MembersAsync(new MemberQuery { GroupField = Conv, GroupKey = "c" + (groups / 2), Filter = filter })).Items.Count);
        await Measure("thread page 1 (biggest group, filter)", async () =>
            (await index.MembersAsync(new MemberQuery { GroupField = Conv, GroupKey = "c0", Filter = filter })).Items.Count);
        string? threadCursor = (await index.MembersAsync(new MemberQuery { GroupField = Conv, GroupKey = "c0", Filter = filter, PageSize = 500 })).Next;
        await Measure("thread page after 500 (biggest group)", async () =>
            (await index.MembersAsync(new MemberQuery { GroupField = Conv, GroupKey = "c0", Filter = filter, After = threadCursor })).Items.Count);

        // Incremental updates: add, change (flip unread / move a date), delete.
        var writes = new List<double>();
        var next = docs;
        for (var i = 0; i < 200; i++)
        {
            var sw = Stopwatch.StartNew();
            await index.AddAsync(Make(next++));
            writes.Add(sw.Elapsed.TotalMilliseconds);
        }

        var addMs = Median(writes);
        writes.Clear();
        for (var i = 0; i < 200; i++)
        {
            var sw = Stopwatch.StartNew();
            await index.AddAsync(Make(rng.NextInt64(0, docs)) with { Id = "m" + rng.NextInt64(0, docs) });
            writes.Add(sw.Elapsed.TotalMilliseconds);
        }

        var changeMs = Median(writes);
        writes.Clear();
        for (var i = 0; i < 200; i++)
        {
            var sw = Stopwatch.StartNew();
            await index.RemoveAsync("m" + (docs - 1 - i));
            writes.Add(sw.Elapsed.TotalMilliseconds);
        }

        var removeMs = Median(writes);
        Console.WriteLine($"  incremental write p50: add {addMs:F2} ms, change {changeMs:F2} ms, delete {removeMs:F2} ms");
        await Measure("list page 1 after updates", async () => (await index.GroupsAsync(ListQuery(null))).Groups.Count);

        if (mdBase is not null)
        {
            var md = new StringBuilder();
            md.AppendLine($"### Grouped queries — {label}");
            md.AppendLine();
            md.AppendLine($"- documents: {docs:N0} in {groups:N0} skewed groups (20% in two groups), indexed in {indexSeconds:F1} s ({docs / indexSeconds:N0} docs/s), no trigram/content/phonetic/stemming");
            md.AppendLine($"- database: {new FileInfo(dbPath).Length / 1048576.0:F0} MiB");
            md.AppendLine($"- incremental write p50: add {addMs:F2} ms, change {changeMs:F2} ms, delete {removeMs:F2} ms");
            md.AppendLine();
            md.AppendLine("| pattern | items | p50 | p95 | max |");
            md.AppendLine("| --- | ---: | ---: | ---: | ---: |");
            foreach (var r in rows)
            {
                md.AppendLine($"| {r.Name} | {r.Items} | {r.P50:F2} ms | {r.P95:F2} ms | {r.Max:F2} ms |");
            }

            md.AppendLine();
            File.AppendAllText(mdBase + ".grouped.md", md.ToString());
        }

        return 0;
    }

    private static double Median(List<double> values)
    {
        var copy = values.OrderBy(v => v).ToList();
        return copy[copy.Count / 2];
    }
}
