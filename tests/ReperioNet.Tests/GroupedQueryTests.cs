using Xunit;

namespace ReperioNet.Tests;

/// <summary>
/// Grouped and member queries (PRD §4a) against a naive in-memory reference model: same groups,
/// order, counts and pages after random adds, changes, removes and re-keys.
/// </summary>
public class GroupedQueryTests
{
    private const string Conv = "conv";

    private static void Declare(ReperioOptions<TestMeta> o)
        => o.Fields.Number("date").Flag("unread").Keys("account").Keys("role").Keys(Conv, orderBy: "date");

    private static Task<SearchIndex<TestMeta>> Open(TestDatabase db) => TestOptions.OpenAsync(db, Declare);

    private static SearchEntry<TestMeta> Entry(string id, long? date, bool unread, string account, string role, params string[] convs)
    {
        var fields = new EntryFields().Keys(Conv, convs).Keys("account", account).Keys("role", role).Flag("unread", unread);
        if (date is { } d)
        {
            fields.Number("date", d);
        }

        return new SearchEntry<TestMeta>(id, "text " + id, new TestMeta("n-" + id, 0), Fields: fields);
    }

    // ---- Reference model --------------------------------------------------------------------

    private sealed class Doc
    {
        public required string Id;
        public long Row;
        public long? Date;
        public bool Unread;
        public string Account = "";
        public string Role = "";
        public string[] Convs = [];
        public long Ord => Date ?? long.MinValue;
    }

    private sealed class Model
    {
        public readonly Dictionary<string, Doc> Docs = new();

        public void Upsert(Doc doc)
        {
            // SQLite INTEGER PRIMARY KEY: existing id keeps its row; a new id gets max(row)+1.
            if (Docs.TryGetValue(doc.Id, out var old))
            {
                doc.Row = old.Row;
            }
            else
            {
                doc.Row = Docs.Count == 0 ? 1 : Docs.Values.Max(d => d.Row) + 1;
            }

            Docs[doc.Id] = doc;
        }

        public void Remove(string id) => Docs.Remove(id);

        public List<(string Key, string NewestId, long Ord, long Count, long Unread)> Groups(Func<Doc, bool> filter)
        {
            var groups = new Dictionary<string, List<Doc>>();
            foreach (var doc in Docs.Values.Where(filter))
            {
                foreach (var key in doc.Convs.Distinct())
                {
                    if (!groups.TryGetValue(key, out var list))
                    {
                        groups[key] = list = [];
                    }

                    list.Add(doc);
                }
            }

            return groups
                .Select(g =>
                {
                    var newest = g.Value.OrderByDescending(d => d.Ord).ThenByDescending(d => d.Row).First();
                    return (g.Key, newest.Id, newest.Ord, (long)g.Value.Count, (long)g.Value.Count(d => d.Unread));
                })
                .OrderByDescending(g => g.Ord)
                .ThenByDescending(g => Docs[g.Id].Row)
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToList();
        }

        public List<Doc> Members(string key, Func<Doc, bool> filter)
            => Docs.Values.Where(filter).Where(d => d.Convs.Contains(key))
                .OrderByDescending(d => d.Ord).ThenByDescending(d => d.Row).ToList();
    }

    private static SearchEntry<TestMeta> ToEntry(Doc d) => Entry(d.Id, d.Date, d.Unread, d.Account, d.Role, d.Convs);

    // ---- Random data and filters ------------------------------------------------------------

    private static Doc RandomDoc(Random rng, string id)
        => new()
        {
            Id = id,
            Date = rng.Next(5) == 0 ? null : rng.Next(0, 30),
            Unread = rng.Next(3) == 0,
            Account = "a" + rng.Next(3),
            Role = "r" + rng.Next(4),
            Convs = Enumerable.Range(0, rng.Next(1, 4)).Select(_ => "k" + rng.Next(12)).Distinct().ToArray(),
        };

    private static (FieldFilter? Filter, Func<Doc, bool> Predicate) RandomFilter(Random rng, int depth = 0)
    {
        switch (rng.Next(depth >= 2 ? 6 : 9))
        {
            case 0:
                return (null, _ => true);
            case 1:
            {
                var accounts = Enumerable.Range(0, 3).Where(_ => rng.Next(2) == 0).Select(i => "a" + i).ToArray();
                return (FieldFilter.KeyIn("account", accounts), d => accounts.Contains(d.Account));
            }

            case 2:
            {
                var roles = Enumerable.Range(0, 4).Where(_ => rng.Next(2) == 0).Select(i => "r" + i).ToArray();
                return (FieldFilter.KeyNotIn("role", roles), d => !roles.Contains(d.Role));
            }

            case 3:
            {
                var value = rng.Next(2) == 0;
                return (FieldFilter.Flag("unread", value), d => d.Unread == value);
            }

            case 4:
            {
                var min = rng.Next(0, 30);
                var max = min + rng.Next(0, 15);
                return (FieldFilter.NumberBetween("date", min, max), d => d.Date is { } x && x >= min && x <= max);
            }

            case 5:
            {
                var key = "k" + rng.Next(12);
                return (FieldFilter.KeyEquals(Conv, key), d => d.Convs.Contains(key));
            }

            case 6:
            {
                var (f1, p1) = RandomFilter(rng, depth + 1);
                var (f2, p2) = RandomFilter(rng, depth + 1);
                return (FieldFilter.And(f1 ?? FieldFilter.And(), f2 ?? FieldFilter.And()), d => p1(d) && p2(d));
            }

            case 7:
            {
                var (f1, p1) = RandomFilter(rng, depth + 1);
                var (f2, p2) = RandomFilter(rng, depth + 1);
                return (FieldFilter.Or(f1 ?? FieldFilter.And(), f2 ?? FieldFilter.And()), d => p1(d) || p2(d));
            }

            default:
            {
                var (f1, p1) = RandomFilter(rng, depth + 1);
                return (FieldFilter.Not(f1 ?? FieldFilter.And()), d => !p1(d));
            }
        }
    }

    // ---- Paging helpers ---------------------------------------------------------------------

    private static async Task<List<GroupPage>> AllGroupPages(SearchIndex<TestMeta> index, FieldFilter? filter, int size)
    {
        var pages = new List<GroupPage>();
        var q = new GroupQuery { GroupField = Conv, Filter = filter, PageSize = size };
        q.CountWhere.Add("unread");
        while (true)
        {
            var page = await index.GroupsAsync(q);
            pages.Add(page);
            if (page.Next is null)
            {
                return pages;
            }

            q.After = page.Next;
            Assert.True(pages.Count < 1000, "paging does not terminate");
        }
    }

    private static async Task AssertGroupsMatch(SearchIndex<TestMeta> index, Model model, FieldFilter? filter, Func<Doc, bool> pred, int size)
    {
        var expected = model.Groups(pred);
        var pages = await AllGroupPages(index, filter, size);
        var actual = pages.SelectMany(p => p.Groups).ToList();
        Assert.Equal(expected.Select(g => g.Key), actual.Select(g => g.Key));
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].NewestId, actual[i].NewestId);
            Assert.Equal(expected[i].Ord, actual[i].NewestOrder);
            Assert.Equal(expected[i].Count, actual[i].Count);
            Assert.Equal(expected[i].Unread, actual[i].FlagCounts["unread"]);
        }

        // Page boundaries: every page but the last is full; the last page has no cursor.
        foreach (var page in pages.Take(pages.Count - 1))
        {
            Assert.Equal(size, page.Groups.Count);
        }

        Assert.Equal(expected.Count == 0 ? 1 : (expected.Count + size - 1) / size, pages.Count);
    }

    private static async Task AssertMembersMatch(SearchIndex<TestMeta> index, Model model, FieldFilter? filter, Func<Doc, bool> pred, string key, int size)
    {
        var expected = model.Members(key, pred);
        var q = new MemberQuery { GroupField = Conv, GroupKey = key, Filter = filter, PageSize = size };
        var actual = new List<MemberHit<TestMeta>>();
        while (true)
        {
            var page = await index.MembersAsync(q);
            actual.AddRange(page.Items);
            if (page.Next is null)
            {
                break;
            }

            Assert.Equal(size, page.Items.Count);
            q.After = page.Next;
        }

        Assert.Equal(expected.Select(d => d.Id), actual.Select(m => m.Id));
        Assert.Equal(expected.Select(d => d.Ord), actual.Select(m => m.Order));
        Assert.All(actual, m => Assert.Equal("n-" + m.Id, m.Metadata.Name));
    }

    // ---- Tests ------------------------------------------------------------------------------

    [Fact]
    public async Task Groups_SmallExample_OrderedByNewestMember_WithCounts()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        await index.AddRangeAsync(
        [
            Entry("m1", 10, true, "a", "inbox", "alice"),
            Entry("m2", 30, false, "a", "inbox", "alice", "bob"),
            Entry("m3", 20, true, "a", "inbox", "bob"),
            Entry("m4", 5, true, "a", "inbox", "carol"),
        ]);

        var q = new GroupQuery { GroupField = Conv };
        q.CountWhere.Add("unread");
        var page = await index.GroupsAsync(q);

        Assert.Null(page.Next);
        Assert.Equal(["alice", "bob", "carol"], page.Groups.Select(g => g.Key));
        Assert.Equal("m2", page.Groups[0].NewestId);
        Assert.Equal(30, page.Groups[0].NewestOrder);
        Assert.Equal(2, page.Groups[0].Count);
        Assert.Equal(1, page.Groups[0].FlagCounts["unread"]);
        Assert.Equal(2, page.Groups[1].Count);
        Assert.Equal(1, page.Groups[2].Count);
    }

    [Fact]
    public async Task Groups_FilterDecidesMembership_AndNewestMember()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        await index.AddRangeAsync(
        [
            Entry("m1", 10, false, "a", "inbox", "alice"),
            Entry("m2", 50, false, "a", "junk", "alice"),
            Entry("m3", 20, false, "b", "inbox", "bob"),
            Entry("m4", 60, false, "a", "junk", "carol"),
        ]);

        var page = await index.GroupsAsync(new GroupQuery
        {
            GroupField = Conv,
            Filter = FieldFilter.KeyNotIn("role", "junk"),
        });

        Assert.Equal(["bob", "alice"], page.Groups.Select(g => g.Key));
        Assert.Equal("m1", page.Groups[1].NewestId); // the junk member does not count
        Assert.Equal(1, page.Groups[1].Count);
    }

    [Fact]
    public async Task Groups_TiesAndMissingOrder_AreTotallyOrdered()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        await index.AddRangeAsync(
        [
            Entry("m1", 7, false, "a", "i", "zed"),
            Entry("m2", 7, false, "a", "i", "yak", "xi"), // same date, later row, two groups
            Entry("m3", null, false, "a", "i", "undated"),
        ]);

        var page = await index.GroupsAsync(new GroupQuery { GroupField = Conv, PageSize = 2 });
        Assert.Equal(["xi", "yak"], page.Groups.Select(g => g.Key));
        var second = await index.GroupsAsync(new GroupQuery { GroupField = Conv, PageSize = 2, After = page.Next });
        Assert.Equal(["zed", "undated"], second.Groups.Select(g => g.Key));
        Assert.Null(second.Next);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task RandomOperations_MatchReferenceModel(int seed)
    {
        var rng = new Random(seed);
        using var db = new TestDatabase();
        await using var index = await Open(db);
        var model = new Model();
        var nextId = 0;

        for (var round = 0; round < 12; round++)
        {
            for (var op = 0; op < 25; op++)
            {
                var kind = rng.Next(10);
                if (kind < 5 || model.Docs.Count == 0)
                {
                    var doc = RandomDoc(rng, "d" + nextId++);
                    model.Upsert(doc);
                    await index.AddAsync(ToEntry(doc));
                }
                else if (kind < 7)
                {
                    // change: same id, new fields
                    var id = model.Docs.Keys.ElementAt(rng.Next(model.Docs.Count));
                    var doc = RandomDoc(rng, id);
                    model.Upsert(doc);
                    await index.AddAsync(ToEntry(doc));
                }
                else if (kind < 9)
                {
                    var id = model.Docs.Keys.ElementAt(rng.Next(model.Docs.Count));
                    model.Remove(id);
                    Assert.True(await index.RemoveAsync(id));
                }
                else
                {
                    // re-key: delete plus add under a new id with the same fields
                    var old = model.Docs.Values.ElementAt(rng.Next(model.Docs.Count));
                    var rekeyed = new Doc
                    {
                        Id = "d" + nextId++, Date = old.Date, Unread = old.Unread, Account = old.Account,
                        Role = old.Role, Convs = old.Convs,
                    };
                    model.Remove(old.Id);
                    await index.RemoveAsync(old.Id);
                    model.Upsert(rekeyed);
                    await index.AddAsync(ToEntry(rekeyed));
                }
            }

            for (var f = 0; f < 4; f++)
            {
                var (filter, pred) = RandomFilter(rng);
                await AssertGroupsMatch(index, model, filter, pred, rng.Next(1, 8));
                var key = "k" + rng.Next(12);
                await AssertMembersMatch(index, model, filter, pred, key, rng.Next(1, 6));
            }
        }
    }

    [Fact]
    public async Task Cursor_UnderConcurrentUpdates_NeverDuplicatesOrSkipsUnchangedGroups()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        var entries = new List<SearchEntry<TestMeta>>();
        for (var g = 0; g < 60; g++)
        {
            entries.Add(Entry($"g{g}-new", 1000 - g * 10, false, "a", "i", "grp" + g));
            entries.Add(Entry($"g{g}-old", 1, false, "a", "i", "grp" + g));
        }

        await index.AddRangeAsync(entries);
        var removedGroups = new HashSet<string>();
        var seen = new List<string>();
        var q = new GroupQuery { GroupField = Conv, PageSize = 7 };
        var step = 0;
        while (true)
        {
            var page = await index.GroupsAsync(q);
            seen.AddRange(page.Groups.Select(g => g.Key));
            if (page.Next is null)
            {
                break;
            }

            q.After = page.Next;
            step++;

            // Between pages: brand-new groups (newer and older than the cursor), older members added
            // to existing groups, an older member removed, and a whole not-yet-seen group removed.
            await index.AddAsync(Entry($"x{step}-newer", 5000 + step, false, "a", "i", "fresh-newer" + step));
            await index.AddAsync(Entry($"x{step}-older", 0, false, "a", "i", "fresh-older" + step));
            await index.AddAsync(Entry($"y{step}", 0, false, "a", "i", "grp" + (step * 3 % 60)));
            await index.RemoveAsync($"g{(step * 5 + 7) % 60}-old");
            var victim = 59 - step;
            if (victim > 10 && removedGroups.Add("grp" + victim))
            {
                await index.RemoveAsync($"g{victim}-new");
                await index.RemoveAsync($"g{victim}-old");
            }
        }

        Assert.Equal(seen.Count, seen.Distinct().Count()); // no duplicates
        for (var g = 0; g < 60; g++)
        {
            var key = "grp" + g;
            if (!removedGroups.Contains(key))
            {
                Assert.Contains(key, seen); // present and with an unchanged newest member: never skipped
            }
        }
    }

    [Fact]
    public async Task Cursor_WithParallelWriter_NeverDuplicatesAndKeepsStablePresentGroups()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        await index.AddRangeAsync(Enumerable.Range(0, 200).Select(g => Entry($"s{g}", 100_000 - g, false, "a", "i", "stable" + g)));

        using var cts = new CancellationTokenSource();
        var writer = Task.Run(async () =>
        {
            var n = 0;
            while (!cts.IsCancellationRequested)
            {
                await index.AddAsync(Entry($"w{n}", n % 2 == 0 ? 200_000 + n : n, false, "a", "i", "churn" + (n % 40)));
                if (n % 3 == 0)
                {
                    await index.RemoveAsync($"w{n - 2}");
                }

                n++;
            }
        });

        var seen = new List<string>();
        var q = new GroupQuery { GroupField = Conv, PageSize = 9 };
        for (var i = 0; i < 40; i++)
        {
            seen.Clear();
            q.After = null;
            while (true)
            {
                var page = await index.GroupsAsync(q);
                seen.AddRange(page.Groups.Select(g => g.Key));
                if (page.Next is null)
                {
                    break;
                }

                q.After = page.Next;
            }

            Assert.Equal(seen.Count, seen.Distinct().Count());
            Assert.Equal(200, seen.Count(k => k.StartsWith("stable", StringComparison.Ordinal)));
        }

        cts.Cancel();
        await writer;
    }

    [Fact]
    public async Task Remove_And_Clear_DropFieldRows()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        await index.AddRangeAsync([Entry("a", 1, true, "x", "i", "k1"), Entry("b", 2, true, "x", "i", "k1")]);

        await index.RemoveAsync("b");
        var page = await index.GroupsAsync(new GroupQuery { GroupField = Conv });
        Assert.Equal(1, Assert.Single(page.Groups).Count);
        Assert.Equal(1, db.QueryScalarLong("SELECT COUNT(*) FROM doc_keys WHERE field = 'conv';"));

        await index.ClearAsync();
        Assert.Empty((await index.GroupsAsync(new GroupQuery { GroupField = Conv })).Groups);
        Assert.Equal(0, db.QueryScalarLong("SELECT COUNT(*) FROM doc_keys;"));
        Assert.Equal(0, db.QueryScalarLong("SELECT COUNT(*) FROM doc_nums;"));
    }

    [Fact]
    public async Task Upsert_ReplacesAllFieldValues()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        await index.AddAsync(Entry("a", 1, true, "x", "i", "old1", "old2"));
        await index.AddAsync(Entry("a", 9, false, "x", "i", "new1"));

        var page = await index.GroupsAsync(new GroupQuery { GroupField = Conv });
        Assert.Equal(["new1"], page.Groups.Select(g => g.Key));
        Assert.Equal(9, page.Groups[0].NewestOrder);
        Assert.Equal(1, db.QueryScalarLong("SELECT COUNT(*) FROM doc_nums WHERE field = 'unread' AND value = 0;"));
    }

    [Fact]
    public async Task Rebuild_KeepsFieldData()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        await index.AddAsync(Entry("a", 1, true, "x", "i", "k1"));
        await index.RebuildAsync();

        Assert.Single((await index.GroupsAsync(new GroupQuery { GroupField = Conv })).Groups);
    }

    [Fact]
    public async Task Members_PagesWithFilter()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        await index.AddRangeAsync(Enumerable.Range(0, 25).Select(i =>
            Entry("m" + i, i, i % 5 == 0, "a", i % 2 == 0 ? "inbox" : "junk", "thread")));

        var q = new MemberQuery
        {
            GroupField = Conv, GroupKey = "thread", PageSize = 4,
            Filter = FieldFilter.KeyEquals("role", "inbox"),
        };
        var ids = new List<string>();
        while (true)
        {
            var page = await index.MembersAsync(q);
            ids.AddRange(page.Items.Select(i => i.Id));
            if (page.Next is null)
            {
                break;
            }

            q.After = page.Next;
        }

        Assert.Equal(Enumerable.Range(0, 25).Where(i => i % 2 == 0).Reverse().Select(i => "m" + i), ids);
    }

    [Fact]
    public async Task Invalid_Requests_Throw()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);
        await index.AddAsync(Entry("a", 1, true, "x", "i", "k1"));

        await Assert.ThrowsAsync<ArgumentException>(() => index.GroupsAsync(new GroupQuery { GroupField = "nope" }));
        await Assert.ThrowsAsync<ArgumentException>(() => index.GroupsAsync(new GroupQuery { GroupField = "account" })); // no orderBy
        await Assert.ThrowsAsync<ArgumentException>(() => index.GroupsAsync(new GroupQuery { GroupField = Conv, PageSize = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => index.GroupsAsync(new GroupQuery { GroupField = Conv, After = "garbage" }));
        await Assert.ThrowsAsync<ArgumentException>(() => index.GroupsAsync(
            new GroupQuery { GroupField = Conv, Filter = FieldFilter.Flag("date", true) }));
        var q = new GroupQuery { GroupField = Conv };
        q.CountWhere.Add("date"); // not a flag
        await Assert.ThrowsAsync<ArgumentException>(() => index.GroupsAsync(q));
        await Assert.ThrowsAsync<ArgumentException>(() => index.MembersAsync(
            new MemberQuery { GroupField = Conv, GroupKey = "k1", After = "!!" }));
    }

    [Fact]
    public async Task Entry_WithUndeclaredOrMistypedField_IsRejected()
    {
        using var db = new TestDatabase();
        await using var index = await Open(db);

        var undeclared = new SearchEntry<TestMeta>("a", "t", new TestMeta("n", 0), Fields: new EntryFields().Flag("nope", true));
        await Assert.ThrowsAsync<ArgumentException>(() => index.AddAsync(undeclared));
        var mistyped = new SearchEntry<TestMeta>("a", "t", new TestMeta("n", 0), Fields: new EntryFields().Number("unread", 1));
        await Assert.ThrowsAsync<ArgumentException>(() => index.AddAsync(mistyped));
        Assert.Equal(0, await index.CountAsync());
    }

    [Fact]
    public async Task Reopen_WithDifferentFieldDeclaration_Throws()
    {
        using var db = new TestDatabase();
        await using (await Open(db))
        {
        }

        var ex = await Assert.ThrowsAsync<ReperioException>(
            () => TestOptions.OpenAsync(db, o => o.Fields.Number("date")));
        Assert.Contains("fields", ex.Message);

        await using var again = await Open(db);
        Assert.Equal("2", db.ReadMeta()["schema_version"]);
        Assert.True(db.TableExists("doc_keys"));
        Assert.True(db.TableExists("doc_nums"));
    }

    [Fact]
    public async Task Declaration_OrderByMustReferenceANumberField()
    {
        using var db = new TestDatabase();
        await Assert.ThrowsAsync<ReperioException>(
            () => TestOptions.OpenAsync(db, o => o.Fields.Keys("c", orderBy: "missing")));
    }
}
