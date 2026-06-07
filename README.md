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

## Options worth knowing

- `StoreContent` (default on): stores one copy of the content — enables snippets and full-text fuzzy re-ranking.
- `EnableTrigram` / `EnableStemming` / `EnablePhonetic` (default on): layout-affecting flags, persisted in the index; reopening with different values throws — call `RebuildAsync()` to migrate.
- `RemoveStopWords` (default off): strips stop words from the stem/phonetic streams only, never from base.
- `MaxContentChars` (default 0 = unbounded): caps the indexed text length.
- `SearchQueryOptions`: `Limit`/`Offset`, `MinScore`, `EnableFuzzy`, `EnablePhonetic`, `Language`, `IncludeSnippet`, `CandidatePoolSize`.

## Operational notes

- **Local storage only**: the index uses SQLite WAL journaling, which is unsafe on network file
  systems (SMB/NFS). Keep the database file on a local disk.
- **One `SearchIndex<TMeta>` instance per database file per process.** All writes are serialized
  internally; reads run concurrently — you will never see `SQLITE_BUSY`.
- Minimum SQLite 3.43.0 with FTS5 — satisfied by the bundled `SQLitePCLRaw.bundle_e_sqlite3` engine
  (the only native artifact, prebuilt for all target platforms including iOS/Android).
- `ReperioNet` and the language packs are trimming/AOT-clean (`net8.0`).
  `ReperioNet.LanguageDetection` depends on NTextCat (an unannotated `netstandard2.0` library
  without a formal trim-compatibility guarantee); in practice the detection path publishes with
  zero IL warnings under both `PublishTrimmed` and Native AOT and works in the resulting binary.

## Samples

- `samples/ReperioNet.Sample.Console` — end-to-end demo; also serves as the trimmed-publish AOT
  smoke test (`dotnet publish -c Release -r <rid> --self-contained -p:PublishTrimmed=true`).
- `samples/ReperioNet.Sample.Maui` — minimal .NET MAUI app (iOS/Android) exercising the index on
  device; build with the MAUI workloads installed (not part of the main solution).

## License

MIT — see [LICENSE](LICENSE).
