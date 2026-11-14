# ReperioNet

> Embedded, fuzzy, multilingual full-text search for .NET, built on SQLite FTS5.
> Pure-managed, cross-platform (Windows / macOS / Linux / Android / iOS), no server, no native build steps.

ReperioNet builds and queries a full-text search index over SQLite FTS5 with a tiny, friendly async
API. It is generic and domain-agnostic: you supply *text plus a strongly-typed metadata payload*,
searches return *metadata plus a relevance score*. Fuzzy search covers typo tolerance (fuzzy
re-ranking), substring/partial matching (trigram index) and spelling variants & word forms
(stemming + phonetic codes) — multilingual out of the box for the European languages.

## Quick start

```csharp
using ReperioNet;
using ReperioNet.Languages.All;
using ReperioNet.LanguageDetection;

var index = await SearchIndex<DocMeta>.OpenAsync("index.db", o =>
{
    o.MetadataTypeInfo = AppJsonContext.Default.DocMeta;   // source-generated, required (AOT-safe)
    o.AddAllEuropeanLanguages();                           // ReperioNet.Languages.All
    o.LanguageDetector = new NTextCatDetector();           // ReperioNet.LanguageDetection (optional)
});

await index.AddAsync(new SearchEntry<DocMeta>(
    Id: doc.Path,
    Content: text,
    Metadata: new DocMeta(doc.Path, name)));

var hits = await index.SearchAsync("müler rechnng");       // typo-tolerant, multilingual
foreach (var h in hits)
    Console.WriteLine($"{h.Score:F2}  {h.Metadata.FileName}");
```

`TMeta` serialization uses System.Text.Json **source generation** — supply a `JsonTypeInfo<TMeta>`
from your own `JsonSerializerContext`. There is no reflection fallback; this keeps the library
trimming- and AOT-clean (iOS/MAUI).

## Packages

| Package | Contents |
| --- | --- |
| `ReperioNet` | Core engine: `SearchIndex<TMeta>`, SQLite FTS5 schema, trigram recall, fuzzy re-ranking, snippets |
| `ReperioNet.Languages.De` … `.Tr` | One pack per language: vendored Snowball stemmer + stop words (German adds Kölner Phonetik, English adds Double Metaphone) |
| `ReperioNet.Languages.All` | Meta-package: `AddAllEuropeanLanguages()` registers all fifteen packs |
| `ReperioNet.LanguageDetection` | `NTextCatDetector` (`ILanguageDetector` backed by NTextCat, Core14 profile bundled) |

Supported language packs: German, English, French, Spanish, Italian, Portuguese, Dutch, Swedish,
Norwegian, Danish, Finnish, Russian, Hungarian, Romanian, Turkish. Unknown or undetected languages
fall back to an identity analyzer — search still works on the base token stream.

## How search works

For each document ReperioNet indexes three token streams (base, stems, phonetic codes) in one FTS5
table plus an optional trigram table for substring/typo recall. A query gathers candidates from all
of them, merges by best bm25 rank, re-ranks the bounded candidate pool with fuzzy similarity
(`0.6 * fuzzy + 0.4 * normalized bm25`, plus an exact-substring boost), then applies `MinScore`,
paging and optional `<mark>`-style snippets. Scores are normalized to 0..1, higher is better.

**Multi-token queries default to all-terms (AND) semantics** (`SearchQueryOptions.TermMatch`):
documents must contain every base term, which matches the common user intent and is far cheaper to
rank than OR (the intersection is small, and FTS5 must bm25-score every matching row). When the
strict pass yields fewer candidates than `Limit`, an any-term pass widens recall automatically —
fallback hits always rank after all-terms hits. Stem/phonetic variant matching and trigram
substring recall keep their OR semantics in the fallback, so inflections and typos are still
caught. Set `TermMatch = TermMatch.AnyTerms` for the widest recall up front.

## Index profiles

Two named presets capture the benchmark-derived recommendations (see
[benchmarks/RESULTS.md](benchmarks/RESULTS.md)); both are chainable extension methods on
`ReperioOptions<TMeta>`:

```csharp
o.UseDesktopProfile();   // = the defaults: trigram + stored content + phonetic, unbounded text
o.UseMobileProfile();    // trigram off, stop words removed, MaxContentChars = 4000
```

- **`UseDesktopProfile()`** — full fidelity. Mid-word substring search (trigram), snippets,
  phonetic variants. Cost: the trigram index is roughly **half the database** (~4.4× raw content
  on the benchmark corpus) and the slowest indexing.
- **`UseMobileProfile()`** — the size/battery-conscious choice. Dropping the trigram index is the
  one change that improves database size (~4.4× → ~2× raw content), query latency and indexing
  throughput *together*. `StoreContent` stays **on** because it is free with respect to size — with
  content off, the same text lives in `rank_text` for fuzzy re-ranking anyway, so turning it off
  saves nothing and only costs snippets. Phonetic codes stay on (cheap, valuable for name
  variants); stop words are removed from the stem/phonetic streams to trim common-term cost; and
  `MaxContentChars = 4000` is the real lever below the rank_text floor for long bodies — a starting
  default you should tune. **Lost:** mid-word substring search. **Kept:** typo tolerance (fuzzy
  re-ranking over content/rank_text), word forms (stemming), phonetic variants, short-query prefix
  matching, snippets.

The flags these presets set are **persisted layout flags**: reopening an existing database with a
different profile throws `ReperioException` (no silent rebuild) — open with the original options
and call `RebuildAsync()` after changing flags, or start a new database file.

## Options worth knowing

- `StoreContent` (default on): stores one copy of the content — enables snippets and full-text fuzzy re-ranking.
- `EnableTrigram` / `EnableStemming` / `EnablePhonetic` (default on): layout-affecting flags, persisted in the index; reopening with different values throws — call `RebuildAsync()` to migrate.
- `RemoveStopWords` (default off): strips stop words from the stem/phonetic streams only, never from base.
- `MaxContentChars` (default 0 = unbounded): caps the indexed text length.
- `EncryptionKey` (default none, 2.2): a 32-byte key that encrypts the index file and its WAL (see *Encryption at rest* below).
- `SearchQueryOptions`: `Limit`/`Offset`, `MinScore`, `EnableFuzzy`, `EnablePhonetic`, `Language`, `IncludeSnippet`, `CandidatePoolSize`, `ScorePoolSize` (score only the best N candidates against their stored text; the rest of the pool is returned behind them with its bm25 share as the score and no snippet, and its text is never loaded: for large pools over long texts).

## Grouped, ordered queries (2.1)

Besides full-text search an index keeps structured fields (multi-valued keys, numbers, flags) so you
can list **groups ordered by their newest member** with exact counts and stable keyset paging, for
example the conversations of a mail client, and page through the members of one group:

```csharp
var index = await SearchIndex<Mail>.OpenAsync("index.db", o =>
{
    o.MetadataTypeInfo = AppJsonContext.Default.Mail;
    o.Fields.Number("date").Flag("unread").Keys("account").Keys("conv", orderBy: "date");
});

await index.AddAsync(new SearchEntry<Mail>(id, text, meta, Fields: new EntryFields()
    .Keys("conv", "alice@x.org").Keys("account", "acc1").Number("date", sent).Flag("unread", true)));

var q = new GroupQuery { GroupField = "conv", PageSize = 50, Filter = FieldFilter.KeyIn("account", "acc1") };
q.CountWhere.Add("unread");
GroupPage page = await index.GroupsAsync(q);        // Key, NewestId, NewestOrder, Count, FlagCounts
q.After = page.Next;                                 // opaque cursor, no gaps or duplicates

MemberPage<Mail> thread = await index.MembersAsync(new MemberQuery { GroupField = "conv", GroupKey = "alice@x.org" });
```

A document may belong to several groups. Filters (`FieldFilter`) combine key, flag and number
predicates with `And`/`Or`/`Not`. Updates, removals and re-keys keep the grouping exact in the same
transaction as the document (no rebuilds). The field declaration is a persisted layout flag, and
2.1 raises the schema version to 2 (an index written by 2.0 must be re-created). See PRD §4a.
At 500k documents in 30k groups a filtered list page of 50 with counts takes about 15 ms and a
thread page under 1 ms (`benchmarks/RESULTS.md`).

## Bulk indexing

`AddRangeAsync` indexes the whole batch in **one transaction** and is heavily optimized for large
loads: SQLite writes stay on the single dedicated write connection (SQLite is single-writer by
nature), but the text analysis — tokenization, stemming, phonetic encoding, metadata
serialization — runs **in parallel across CPU cores** ahead of the writer, with stem/phonetic
results memoized for the duration of the batch. During a bulk batch the write connection also runs
with a temporarily enlarged page cache (32 MiB) and a raised WAL checkpoint threshold, both
restored when the batch completes. Entries are written strictly in input order (for duplicate ids
the last one wins), and an invalid entry rolls back the entire batch.

One contract follows from this: `IStemmer`, `IPhoneticEncoder`, `IStopWordFilter` and
`ILanguageDetector` implementations must be **thread-safe** (all bundled implementations are; they
keep no mutable state).

Practical tips for big loads: prefer one large `AddRangeAsync` over many `AddAsync` calls, pass
batches of ~10k–50k entries per call for progress reporting, call `OptimizeAsync()` once at the
end, and consider the index-layout options below — the trigram index is the dominant cost in both
indexing time and database size.

## Benchmarks

`benchmarks/ReperioNet.Benchmark` is a combined scale smoke test and benchmark. It generates
deterministic email-like documents (~860 bytes, en/de/fr mix), bulk-indexes them, asserts
correctness at scale (needle recall and ranking, stemming, phonetic, substring, snippets, CRUD
consistency), then measures a search-latency battery (cold + p50/p95/max), concurrent throughput,
single-document mutation latency, peak process memory and raw-content-vs-database size.

```bash
# single run (defaults: 1,000,000 docs, profile "full")
dotnet run -c Release --project benchmarks/ReperioNet.Benchmark -- --docs 100000

# full matrix: 4 index-layout profiles x 3 simulated device classes -> benchmarks/RESULTS.md
benchmarks/run-matrix.sh 100000
```

Index-layout profiles (`--profile`): `full` (trigram + stemming + phonetic + stored content — best
recall, biggest database), `no-trigram` (drops substring recall), `compact` (additionally stores no
content copy — no snippets, fuzzy re-ranks on `rank_text`), `smallest` (additionally no phonetic
codes, stop words removed from the stem stream). Device classes are simulated by restricting CPU
affinity (`taskset`): desktop (all cores), fast phone (4 cores), slow phone (2 cores) — note this
models reduced parallelism, not slower silicon or flash storage. Results, including the exact CPU,
memory consumption and db/content ratios for every combination, are checked in at
[benchmarks/RESULTS.md](benchmarks/RESULTS.md).

## Encryption at rest (2.2)

Set `o.EncryptionKey` to 32 random bytes to keep the index encrypted on disk (ChaCha20-Poly1305, the default cipher of
[SQLite3 Multiple Ciphers](https://github.com/utelle/SQLite3MultipleCiphers), MIT). The process must run on that SQLite build:
reference `SQLite3MC.PCLRaw.bundle` and exclude the stock engine (one SQLite build per process):

```xml
<PackageReference Include="SQLite3MC.PCLRaw.bundle" Version="2.6.1" />
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.14" ExcludeAssets="all" PrivateAssets="all" />
<PackageReference Include="SQLitePCLRaw.lib.e_sqlite3" Version="2.1.14" ExcludeAssets="all" PrivateAssets="all" />
<PackageReference Include="SQLitePCLRaw.provider.e_sqlite3" Version="2.1.14" ExcludeAssets="all" PrivateAssets="all" />
```

- On any other SQLite build `OpenAsync` throws `ReperioException` **before anything is written**: a build without encryption would
  ignore the key and keep the index in plaintext.
- Give the same key on every open. A wrong key throws `ReperioException` (for a search index the usual answer is to delete the
  file and rebuild). A key that is not exactly 32 bytes throws `ArgumentException`.
- Keeping the key (an OS keystore, a key file) is the caller's job. Microsoft.Data.Sqlite applies it as `PRAGMA key` on every
  physical connection, so it lives in the connection string in memory as hex for the lifetime of the index.
- Without a key nothing changes: the index is a plain SQLite file on either build.

## Operational notes

- **Local storage only**: the index uses SQLite WAL journaling, which is unsafe on network file
  systems (SMB/NFS). Keep the database file on a local disk.
- **One `SearchIndex<TMeta>` instance per database file per process.** All writes are serialized
  internally; reads run concurrently — you will never see `SQLITE_BUSY`.
- Minimum SQLite 3.43.0 with FTS5 — satisfied by the bundled `SQLitePCLRaw.bundle_e_sqlite3` engine
  (the only native artifact, prebuilt for all target platforms including iOS/Android).
- `ReperioNet` and the language packs are trimming/AOT-clean (`net10.0`).
  `ReperioNet.LanguageDetection` depends on NTextCat (an unannotated `netstandard2.0` library
  without a formal trim-compatibility guarantee); in practice the detection path publishes with
  zero IL warnings under both `PublishTrimmed` and Native AOT and works in the resulting binary.

## Samples

- `samples/ReperioNet.Sample.Console` — end-to-end demo; also serves as the trimmed-publish AOT
  smoke test (`dotnet publish -c Release -r <rid> --self-contained -p:PublishTrimmed=true`).
- `samples/ReperioNet.Sample.Maui` — minimal .NET MAUI app (iOS/Android) exercising the index on
  device; build with the MAUI workloads installed (not part of the main solution).

## License

MIT — see [LICENSE](LICENSE). The language packs contain C# ports of the Snowball stemming
algorithms (BSD 3-clause, © Dr Martin Porter / Richard Boulton) and other derived material; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the full notices.
