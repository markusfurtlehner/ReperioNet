using System.Diagnostics;
using System.Text;
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
//   --profile P     index layout profile: full | no-trigram | compact | smallest (default full)
//   --label TEXT    run label used in reports (default: profile name)
//   --md BASEPATH   append results to BASEPATH.summary.md (one table row) and
//                   BASEPATH.details.md (a full section) for report assembly
//   --skip-index    reuse an existing database (skip generation + indexing)
//   --keep          keep the database file afterwards
//   --no-trigram    legacy alias: force the trigram index off
//
// Profiles (PRD §5 layout flags; each yields a different database size):
//   full        trigram + stemming + phonetic + stored content   (best recall, biggest db)
//   no-trigram  no substring recall                              (smaller)
//   compact     additionally StoreContent=false                  (no snippets, fuzzy uses rank_text)
//   smallest    additionally no phonetic + stop words removed    (smallest db)

var docs = 1_000_000L;
var batch = 25_000;
var iters = 20;
var dbPath = "/tmp/reperionet-bench/index.db";
var profile = "full";
string? label = null;
string? mdBase = null;
var skipIndex = false;
var keep = false;
var trigramOverride = (bool?)null;
for (var a = 0; a < args.Length; a++)
{
    switch (args[a])
    {
        case "--docs": docs = long.Parse(args[++a]); break;
        case "--batch": batch = int.Parse(args[++a]); break;
        case "--iters": iters = int.Parse(args[++a]); break;
        case "--db": dbPath = args[++a]; break;
        case "--profile": profile = args[++a]; break;
        case "--label": label = args[++a]; break;
        case "--md": mdBase = args[++a]; break;
        case "--skip-index": skipIndex = true; break;
        case "--keep": keep = true; break;
        case "--no-trigram": trigramOverride = false; break;
        default: Console.Error.WriteLine($"unknown arg {args[a]}"); return 2;
    }
}

var (trigram, storeContent, phonetic, removeStopWords) = profile switch
{
    "full" => (true, true, true, false),
    "no-trigram" => (false, true, true, false),
    "compact" => (false, false, true, false),
    "smallest" => (false, false, false, true),
    _ => throw new ArgumentException($"unknown profile '{profile}'"),
};
trigram = trigramOverride ?? trigram;
label ??= profile;

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
if (!skipIndex)
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        File.Delete(dbPath + suffix);
    }
}

var (cpuModel, cpuMhz) = CpuInfo();
Console.WriteLine($"ReperioNet benchmark — label={label} profile={profile} docs={docs:N0} batch={batch:N0} iters={iters}");
Console.WriteLine($"layout: trigram={trigram} storeContent={storeContent} phonetic={phonetic} removeStopWords={removeStopWords}");
Console.WriteLine($"cpu: {Environment.ProcessorCount} usable core(s), {cpuModel} @ {cpuMhz} MHz");
Console.WriteLine($"db: {dbPath}");
Console.WriteLine();

var total = Stopwatch.StartNew();
var failures = 0;
var contentBytes = 0L;
var indexDocsPerSecond = 0d;
var optimizeSeconds = 0d;
double concurrentQps;
double addMs, removeMs;
var latencyRows = new List<(string Name, int Hits, double Cold, double P50, double P95, double Max)>();

await using (var index = await SearchIndex<EmailMeta>.OpenAsync(dbPath, o =>
{
    o.MetadataTypeInfo = BenchmarkJsonContext.Default.EmailMeta;
    o.AddAllEuropeanLanguages();
    o.DefaultLanguage = "en";
    o.EnableTrigram = trigram;
    o.StoreContent = storeContent;
    o.EnablePhonetic = phonetic;
    o.RemoveStopWords = removeStopWords;
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
            await index.AddRangeAsync(EmailCorpus.Range(start, count).Select(entry =>
            {
                contentBytes += Encoding.UTF8.GetByteCount(entry.Content);
                return entry;
            }));
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
        indexDocsPerSecond = docs / indexing.Elapsed.TotalSeconds;
        Console.WriteLine($"  indexed {docs:N0} docs (+3 planted) in {indexing.Elapsed:hh\\:mm\\:ss} = {indexDocsPerSecond:N0} docs/s");

        var optimize = Stopwatch.StartNew();
        await index.OptimizeAsync();
        optimizeSeconds = optimize.Elapsed.TotalSeconds;
        Console.WriteLine($"  OptimizeAsync: {optimizeSeconds:N1} s");
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

    if (storeContent)
    {
        var snippet = await index.SearchAsync(
            needleQuery,
            new SearchQueryOptions { IncludeSnippet = true, EnablePhonetic = false });
        Check(snippet.Count == 1 && snippet[0].Snippet?.Contains("<mark>") == true, "needle snippet contains <mark>");
    }

    var stem = await index.SearchAsync("Verlängerung", new SearchQueryOptions { Language = "de" });
    Check(stem.Any(h => h.Id == "special-stem"), "de stemming: Verlängerung -> Verlängerungen");

    if (phonetic)
    {
        var phon = await index.SearchAsync("Witgenstain", new SearchQueryOptions { Language = "de" });
        Check(phon.Any(h => h.Id == "special-phon"), "de phonetic: Witgenstain -> Wittgenstein");
    }

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
        var p50 = Percentile(samples, 50);
        var p95 = Percentile(samples, 95);
        latencyRows.Add((name, hits, cold, p50, p95, samples[^1]));
        Console.WriteLine($"  {name,-34} {hits,6} {cold,7:N1}ms {p50,7:N1}ms {p95,7:N1}ms {samples[^1],7:N1}ms");
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
    concurrentQps = 200 / concurrent.Elapsed.TotalSeconds;
    Console.WriteLine($"  200 queries in {concurrent.Elapsed.TotalSeconds:N1} s = {concurrentQps:N1} queries/s");
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

    addMs = addTimes.Average();
    removeMs = removeTimes.Average();
    Console.WriteLine($"  AddAsync (upsert): {addMs:N1} ms   RemoveAsync: {removeMs:N1} ms");
}

var dbBytes = new[] { "", "-wal", "-shm" }.Sum(s => File.Exists(dbPath + s) ? new FileInfo(dbPath + s).Length : 0);
var peakRss = PeakRssBytes();
Console.WriteLine();
if (contentBytes > 0)
{
    Console.WriteLine(
        $"raw content:   {contentBytes,16:N0} bytes ({contentBytes / 1024.0 / 1024.0:N1} MiB, " +
        $"{contentBytes / (double)docs:N0} bytes/doc avg)");
    Console.WriteLine(
        $"database file: {dbBytes,16:N0} bytes ({dbBytes / 1024.0 / 1024.0 / 1024.0:N2} GiB, " +
        $"{dbBytes / (double)docs:N0} bytes/doc)");
    Console.WriteLine($"overhead:      db/content ratio {(double)dbBytes / contentBytes:N2}x");
}
else
{
    Console.WriteLine($"database size: {dbBytes:N0} bytes ({dbBytes / 1024.0 / 1024.0 / 1024.0:N2} GiB)");
}

Console.WriteLine($"peak process memory: {peakRss / 1024.0 / 1024.0:N0} MiB");
Console.WriteLine($"total wall time: {total.Elapsed:hh\\:mm\\:ss}");

if (mdBase is not null)
{
    var smoke = failures == 0 ? "pass" : $"FAIL ({failures})";
    var ratio = contentBytes > 0 ? $"{(double)dbBytes / contentBytes:N2}x" : "n/a";
    var needleP50 = latencyRows.First(r => r.Name.StartsWith("needle, phonetic off")).P50;
    var commonP50 = latencyRows.First(r => r.Name.StartsWith("common term (~")).P50;

    File.AppendAllText(
        mdBase + ".summary.md",
        $"| {label} | {profile} | {Environment.ProcessorCount} | {indexDocsPerSecond:N0} | {optimizeSeconds:N1} s " +
        $"| {contentBytes / 1024.0 / 1024.0:N1} MiB | {dbBytes / 1024.0 / 1024.0:N1} MiB | {ratio} " +
        $"| {needleP50:N1} ms | {commonP50:N1} ms | {concurrentQps:N1} | {addMs:N1} ms | {peakRss / 1024.0 / 1024.0:N0} MiB | {smoke} |\n");

    var details = new StringBuilder();
    details.AppendLine($"### {label}");
    details.AppendLine();
    details.AppendLine($"- profile: `{profile}` (trigram={trigram}, storeContent={storeContent}, phonetic={phonetic}, removeStopWords={removeStopWords})");
    details.AppendLine($"- usable cores: {Environment.ProcessorCount} ({cpuModel} @ {cpuMhz} MHz)");
    details.AppendLine($"- documents: {docs:N0} (+3 planted), batch size {batch:N0}");
    details.AppendLine($"- indexing: **{indexDocsPerSecond:N0} docs/s**, OptimizeAsync {optimizeSeconds:N1} s");
    details.AppendLine($"- raw content: {contentBytes:N0} bytes ({contentBytes / 1024.0 / 1024.0:N1} MiB) — database: {dbBytes:N0} bytes ({dbBytes / 1024.0 / 1024.0:N1} MiB) — **ratio {ratio}**");
    details.AppendLine($"- peak process memory: {peakRss / 1024.0 / 1024.0:N0} MiB");
    details.AppendLine($"- concurrent throughput: {concurrentQps:N1} queries/s (8 workers) — AddAsync {addMs:N1} ms, RemoveAsync {removeMs:N1} ms");
    details.AppendLine($"- smoke checks: {smoke}");
    details.AppendLine();
    details.AppendLine("| pattern | hits | cold | p50 | p95 | max |");
    details.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
    foreach (var (name, hits, cold, p50, p95, max) in latencyRows)
    {
        details.AppendLine($"| {name} | {hits} | {cold:N1} ms | {p50:N1} ms | {p95:N1} ms | {max:N1} ms |");
    }

    details.AppendLine();
    File.AppendAllText(mdBase + ".details.md", details.ToString());
}

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

static long PeakRssBytes()
{
    try
    {
        foreach (var line in File.ReadLines("/proc/self/status"))
        {
            if (line.StartsWith("VmHWM:", StringComparison.Ordinal))
            {
                var kib = long.Parse(line[6..].Trim().TrimEnd('k', 'B', ' '));
                return kib * 1024;
            }
        }
    }
    catch (IOException)
    {
        // Not on Linux or /proc unavailable.
    }

    return Environment.WorkingSet;
}

static (string Model, string Mhz) CpuInfo()
{
    var model = "unknown";
    var mhz = "unknown";
    try
    {
        foreach (var line in File.ReadLines("/proc/cpuinfo"))
        {
            if (model == "unknown" && line.StartsWith("model name", StringComparison.Ordinal))
            {
                model = line[(line.IndexOf(':') + 1)..].Trim();
            }
            else if (mhz == "unknown" && line.StartsWith("cpu MHz", StringComparison.Ordinal))
            {
                mhz = line[(line.IndexOf(':') + 1)..].Trim();
            }

            if (model != "unknown" && mhz != "unknown")
            {
                break;
            }
        }
    }
    catch (IOException)
    {
        // Not on Linux or /proc unavailable.
    }

    return (model, mhz);
}
