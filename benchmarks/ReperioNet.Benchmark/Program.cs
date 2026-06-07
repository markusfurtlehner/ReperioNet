using System.Diagnostics;
using ReperioNet;
using ReperioNet.Benchmark;
using ReperioNet.Languages.All;

// ReperioNet scale smoke test + search benchmark.
//
//   dotnet run -c Release --project benchmarks/ReperioNet.Benchmark -- [options]
//
//   --docs N        number of generated documents (default 1,000,000)
//   --batch N       documents per AddRangeAsync transaction (default 25,000)
//   --iters N       timed iterations per query pattern (default 20)
//   --db PATH       database file (default /tmp/reperionet-bench/index.db)
//   --skip-index    reuse an existing database (skip generation + indexing)
//   --keep          keep the database file afterwards
//   --no-trigram    build without the trigram index (smaller/faster, no substring recall)

var docs = 1_000_000L;
var batch = 25_000;
var iters = 20;
var dbPath = "/tmp/reperionet-bench/index.db";
var skipIndex = false;
var keep = false;
var trigram = true;
for (var a = 0; a < args.Length; a++)
{
    switch (args[a])
    {
        case "--docs": docs = long.Parse(args[++a]); break;
        case "--batch": batch = int.Parse(args[++a]); break;
        case "--iters": iters = int.Parse(args[++a]); break;
        case "--db": dbPath = args[++a]; break;
        case "--skip-index": skipIndex = true; break;
        case "--keep": keep = true; break;
        case "--no-trigram": trigram = false; break;
        default: Console.Error.WriteLine($"unknown arg {args[a]}"); return 2;
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
if (!skipIndex)
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        File.Delete(dbPath + suffix);
    }
}

Console.WriteLine($"ReperioNet benchmark — docs={docs:N0} batch={batch:N0} iters={iters} trigram={trigram}");
Console.WriteLine($"db: {dbPath}");
Console.WriteLine();

var total = Stopwatch.StartNew();
var failures = 0;

await using (var index = await SearchIndex<EmailMeta>.OpenAsync(dbPath, o =>
{
    o.MetadataTypeInfo = BenchmarkJsonContext.Default.EmailMeta;
    o.AddAllEuropeanLanguages();
    o.DefaultLanguage = "en";
    o.EnableTrigram = trigram;
}))
{
    // ---- Phase 1: bulk indexing -------------------------------------------------------------
    if (!skipIndex)
    {
        Console.WriteLine("[1/5] indexing");
        var indexing = Stopwatch.StartNew();
        for (long start = 0; start < docs; start += batch)
        {
            var count = Math.Min(batch, docs - start);
            var batchTimer = Stopwatch.StartNew();
            await index.AddRangeAsync(EmailCorpus.Range(start, count));
            var done = start + count;
            var eta = TimeSpan.FromSeconds(indexing.Elapsed.TotalSeconds / done * (docs - done));
            Console.WriteLine(
                $"  {done,12:N0} / {docs:N0}   batch {count / batchTimer.Elapsed.TotalSeconds,8:N0} docs/s   " +
                $"cumulative {done / indexing.Elapsed.TotalSeconds,8:N0} docs/s   eta {eta:hh\\:mm\\:ss}");
        }

        // Planted documents with vocabulary that exists nowhere in the generated corpus, so the
        // smoke assertions below have unambiguous answers even among a million documents.
        await index.AddAsync(new SearchEntry<EmailMeta>(
            "special-stem",
            "Die beantragten Verlängerungen der Lizenzen wurden genehmigt. uidspecial1",
            new EmailMeta("Lizenzen", "legal@example.com"),
            "de"));
        await index.AddAsync(new SearchEntry<EmailMeta>(
            "special-phon",
            "Herr Wittgenstein hat die Unterlagen unterschrieben. uidspecial2",
            new EmailMeta("Unterschrift", "office@example.com"),
            "de"));
        await index.AddAsync(new SearchEntry<EmailMeta>(
            "special-trgm",
            "The attached Quintessenzanalyse covers the laboratory results. uidspecial3",
            new EmailMeta("Lab results", "lab@example.com"),
            "en"));

        indexing.Stop();
        var rate = docs / indexing.Elapsed.TotalSeconds;
        Console.WriteLine($"  indexed {docs:N0} docs (+3 planted) in {indexing.Elapsed:hh\\:mm\\:ss} = {rate:N0} docs/s");

        var optimize = Stopwatch.StartNew();
        await index.OptimizeAsync();
        Console.WriteLine($"  OptimizeAsync: {optimize.Elapsed.TotalSeconds:N1} s");
    }
    else
    {
        Console.WriteLine("[1/5] indexing skipped (reusing existing database)");
    }

    Console.WriteLine();

    // ---- Phase 2: smoke checks (correctness at scale) ---------------------------------------
    Console.WriteLine("[2/5] smoke checks");
    var expectedCount = docs + 3;
    Check(await index.CountAsync() == expectedCount, $"CountAsync == {expectedCount:N0}");

    var needleId = $"mail-{docs / 2:D7}";
    var needleQuery = EmailCorpus.UniqueToken(docs / 2);

    // With phonetic off the uid token is genuinely unique -> exact recall.
    var needleExact = await index.SearchAsync(needleQuery, new SearchQueryOptions { EnablePhonetic = false });
    Check(needleExact.Count == 1 && needleExact[0].Id == needleId, $"needle uid (phonetic off) -> exactly {needleId}");

    // With phonetic on, every uid token shares one phonetic code (encoders ignore digits), so the
    // clause recalls thousands of collision docs — the true match must still rank first.
    var needleRanked = await index.SearchAsync(needleQuery);
    Check(needleRanked.Count >= 1 && needleRanked[0].Id == needleId, "needle uid ranks #1 despite phonetic-code collisions");

    var snippet = await index.SearchAsync(
        needleQuery,
        new SearchQueryOptions { IncludeSnippet = true, EnablePhonetic = false });
    Check(snippet.Count == 1 && snippet[0].Snippet?.Contains("<mark>") == true, "needle snippet contains <mark>");

    var stem = await index.SearchAsync("Verlängerung", new SearchQueryOptions { Language = "de" });
    Check(stem.Any(h => h.Id == "special-stem"), "de stemming: Verlängerung -> Verlängerungen");

    var phon = await index.SearchAsync("Witgenstain", new SearchQueryOptions { Language = "de" });
    Check(phon.Any(h => h.Id == "special-phon"), "de phonetic: Witgenstain -> Wittgenstein");

    if (trigram)
    {
        var substring = await index.SearchAsync("essenzanal");
        Check(substring.Count == 1 && substring[0].Id == "special-trgm", "trigram substring: essenzanal");
    }

    Check(await index.ContainsAsync(needleId), $"ContainsAsync({needleId})");
    Check(!await index.ContainsAsync("no-such-id"), "ContainsAsync(no-such-id) == false");

    Check(await index.RemoveAsync("special-trgm"), "RemoveAsync(special-trgm)");
    Check(await index.CountAsync() == expectedCount - 1, "count reflects removal");
    await index.AddAsync(new SearchEntry<EmailMeta>(
        "special-trgm",
        "The attached Quintessenzanalyse covers the laboratory results. uidspecial3",
        new EmailMeta("Lab results", "lab@example.com"),
        "en"));
    Check(await index.CountAsync() == expectedCount, "re-add restores count");
    Console.WriteLine();

    // ---- Phase 3: search latency battery ----------------------------------------------------
    Console.WriteLine($"[3/5] search latency ({iters} iterations each, after 1 recorded cold run + 2 warmups)");
    Console.WriteLine($"  {"pattern",-34} {"hits",6} {"cold",9} {"p50",9} {"p95",9} {"max",9}");

    var battery = new (string Name, string Query, SearchQueryOptions? Options)[]
    {
        ("needle, phonetic off", EmailCorpus.UniqueToken(123_456 % docs), new SearchQueryOptions { EnablePhonetic = false }),
        ("needle (uid; phonetic collides)", EmailCorpus.UniqueToken(123_456 % docs), null),
        ("common term (~50% of docs)", "invoice", null),
        ("common term, fuzzy off", "invoice", new SearchQueryOptions { EnableFuzzy = false }),
        ("common term + snippets", "invoice", new SearchQueryOptions { IncludeSnippet = true }),
        ("common term, page offset 100", "invoice", new SearchQueryOptions { Offset = 100, Limit = 50 }),
        ("de stemmed inflection", "rechnungen", new SearchQueryOptions { Language = "de" }),
        ("de phonetic variant", "Witgenstain", new SearchQueryOptions { Language = "de" }),
        ("multi-token OR", "monthly invoice payment", null),
        ("multi-token with typo", "monthly invoce", null),
        ("short query (prefix aid)", "re", null),
        ("no-match term", "zzzzqqqq", null),
    };

    foreach (var (name, query, options) in battery)
    {
        var hits = 0;
        var cold = await TimeAsync(async () => hits = (await index.SearchAsync(query, options)).Count);
        for (var w = 0; w < 2; w++)
        {
            await index.SearchAsync(query, options);
        }

        var samples = new double[iters];
        for (var k = 0; k < iters; k++)
        {
            samples[k] = await TimeAsync(async () => await index.SearchAsync(query, options));
        }

        Array.Sort(samples);
        Console.WriteLine(
            $"  {name,-34} {hits,6} {cold,7:N1}ms {Percentile(samples, 50),7:N1}ms " +
            $"{Percentile(samples, 95),7:N1}ms {samples[^1],7:N1}ms");
    }

    Console.WriteLine();

    // ---- Phase 4: concurrent search throughput ----------------------------------------------
    Console.WriteLine("[4/5] concurrent search throughput (8 workers x 25 mixed queries)");
    var mixed = battery.Where(b => !b.Name.StartsWith("no-match")).ToArray();
    var concurrent = Stopwatch.StartNew();
    await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
    {
        for (var q = 0; q < 25; q++)
        {
            var (_, query, options) = mixed[(worker + q) % mixed.Length];
            await index.SearchAsync(query, options);
        }
    })));
    concurrent.Stop();
    Console.WriteLine($"  200 queries in {concurrent.Elapsed.TotalSeconds:N1} s = {200 / concurrent.Elapsed.TotalSeconds:N0} queries/s");
    Console.WriteLine();

    // ---- Phase 5: mutations against the full index ------------------------------------------
    Console.WriteLine("[5/5] single-document mutations at full size (avg of 10)");
    var addTimes = new double[10];
    for (var k = 0; k < addTimes.Length; k++)
    {
        addTimes[k] = await TimeAsync(() => index.AddAsync(EmailCorpus.Create(docs + 100 + k)));
    }

    var removeTimes = new double[10];
    for (var k = 0; k < removeTimes.Length; k++)
    {
        removeTimes[k] = await TimeAsync(() => index.RemoveAsync($"mail-{docs + 100 + k:D7}"));
    }

    Console.WriteLine($"  AddAsync (upsert): {addTimes.Average():N1} ms   RemoveAsync: {removeTimes.Average():N1} ms");
}

var dbBytes = new[] { "", "-wal", "-shm" }.Sum(s => File.Exists(dbPath + s) ? new FileInfo(dbPath + s).Length : 0);
Console.WriteLine();
Console.WriteLine($"database size: {dbBytes / 1024.0 / 1024.0 / 1024.0:N2} GiB   total wall time: {total.Elapsed:hh\\:mm\\:ss}");

if (!keep)
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        File.Delete(dbPath + suffix);
    }
}

Console.WriteLine(failures == 0 ? "SMOKE TEST PASSED" : $"SMOKE TEST FAILED ({failures} check(s))");
return failures == 0 ? 0 : 1;

void Check(bool condition, string description)
{
    Console.WriteLine($"  {(condition ? "ok  " : "FAIL")}  {description}");
    if (!condition)
    {
        failures++;
    }
}

static async Task<double> TimeAsync(Func<Task> action)
{
    var stopwatch = Stopwatch.StartNew();
    await action();
    return stopwatch.Elapsed.TotalMilliseconds;
}

static double Percentile(double[] sorted, int percentile)
    => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1)];
