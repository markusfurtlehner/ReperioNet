# ReperioNet — Product Requirements Document (Implementation Spec)

> **ReperioNet** — embedded, fuzzy, multilingual full-text search for .NET, built on SQLite FTS5.
> Pure-managed, cross-platform (Windows / macOS / Linux / Android / iOS), no server, no native build steps.

This document is the complete, authoritative spec for an implementing agent (e.g. Claude Code). **Every design decision is fixed here; there are no open questions.** Requirements use MUST/SHOULD. Section 14 records the rationale for the non-obvious decisions.

---

## 1. Goals & Non-Goals

### Goals
1. A reusable .NET library that builds and queries a **full-text search index over SQLite FTS5** with a tiny, friendly async API.
2. **Generic and domain-agnostic**: the consumer supplies *text + a strongly-typed metadata payload*; the library returns *metadata + a relevance score*. The library knows nothing about emails, files, PDFs, etc.
3. **Fuzzy search** covering three needs simultaneously: **typo tolerance** (edit distance), **substring / partial matching**, and **spelling variants & word forms** (stemming + phonetic).
4. **Multilingual** out of the box for the **European languages** (full Snowball set), with everything behind interfaces so languages can be **swapped or extended**.
5. **Cross-platform incl. mobile**: pure-managed, AOT/trimming-safe, no native dependencies beyond the bundled SQLite native lib.

### Non-Goals
- No dedicated server / daemon (mobile forbids it).
- No CJK word segmentation beyond incidental trigram matching.
- No vector / semantic / embedding search.
- No document parsing (PDF, .eml, .docx, …) and no OCR. That is the consumer's job.

---

## 2. Target Framework & Platform Constraints

- **Target framework: `net8.0` only.** netstandard2.0 is explicitly NOT targeted (keeps the AOT/trimming story clean; MAUI mobile targets are net8.0-based). See §14.1.
- All code MUST be **pure managed**. The only native artifact is the SQLite engine, supplied transitively via `SQLitePCLRaw.bundle_e_sqlite3` (precompiled for all target platforms incl. iOS/Android).
- **AOT / trimming safety is a hard requirement** (iOS uses full AOT; MAUI trims aggressively):
  - **No reflection-based discovery / assembly scanning.** Language packs register **explicitly** via extension methods (§7).
  - `TMeta` JSON serialization uses **System.Text.Json source generation**. `ReperioOptions<TMeta>.MetadataTypeInfo` (a `JsonTypeInfo<TMeta>`) is **required**: if it is null, `OpenAsync` MUST throw `ReperioException` instructing the caller to supply a source-generated `JsonTypeInfo`. No reflection-based serialization fallback is provided. See §14.2.
  - Use `[DynamicallyAccessedMembers]` / trim annotations only where unavoidable; the library MUST publish trim-clean (no trim warnings) and AOT-clean.

---

## 3. Package & Namespace Layout

```
ReperioNet                       (core; namespaces ReperioNet, ReperioNet.Abstractions)
ReperioNet.Languages.De          (German    analyzer; namespace ReperioNet.Languages.De)
ReperioNet.Languages.En          (English)
ReperioNet.Languages.Fr          (French)
ReperioNet.Languages.Es          (Spanish)
ReperioNet.Languages.It          (Italian)
ReperioNet.Languages.Pt          (Portuguese)
ReperioNet.Languages.Nl          (Dutch)
ReperioNet.Languages.Sv          (Swedish)
ReperioNet.Languages.No          (Norwegian)
ReperioNet.Languages.Da          (Danish)
ReperioNet.Languages.Fi          (Finnish)
ReperioNet.Languages.Ru          (Russian)
ReperioNet.Languages.Hu          (Hungarian)
ReperioNet.Languages.Ro          (Romanian)
ReperioNet.Languages.Tr          (Turkish)        # Snowball supports it; European-adjacent, include it
ReperioNet.Languages.All         (meta-package; references every language pack; AddAllEuropeanLanguages())
ReperioNet.LanguageDetection     (ILanguageDetector backed by NTextCat)
```

The full set of language packs MUST cover every language the Snowball algorithm supports in the European group. Each pack name uses the ISO 639-1 code in PascalCase.

**Dependencies**
- `ReperioNet` → `Microsoft.Data.Sqlite`, `FuzzySharp`.
- `ReperioNet.Languages.*` → `ReperioNet`. Each pack **vendors its own pure-managed Snowball stemmer** (a port of the official Snowball algorithm for that language). No external Snowball NuGet dependency. See §14.6.
- `ReperioNet.LanguageDetection` → `ReperioNet`, `NTextCat`.

**Repo layout**
```
/src/<each package>
/tests/ReperioNet.Tests
/tests/ReperioNet.Languages.Tests
/samples/ReperioNet.Sample.Console
/samples/ReperioNet.Sample.Maui        (iOS/Android AOT smoke test)
/.github/workflows/ci.yml
README.md  LICENSE (MIT)
```

---

## 4. Public API (core package `ReperioNet`)

All I/O methods are `async` and accept a `CancellationToken`. SQLite I/O is synchronous under the hood; the async surface keeps callers off the UI thread and routes through the single-writer gate (§8).

```csharp
namespace ReperioNet;

public sealed class SearchIndex<TMeta> : IAsyncDisposable
{
    public static Task<SearchIndex<TMeta>> OpenAsync(
        string databasePath,
        Action<ReperioOptions<TMeta>>? configure = null,
        CancellationToken ct = default);

    public Task AddAsync(SearchEntry<TMeta> entry, CancellationToken ct = default);            // upsert by Id
    public Task AddRangeAsync(IEnumerable<SearchEntry<TMeta>> entries, CancellationToken ct = default); // one transaction
    public Task<bool> RemoveAsync(string id, CancellationToken ct = default);                  // false if absent
    public Task<bool> ContainsAsync(string id, CancellationToken ct = default);
    public Task<long> CountAsync(CancellationToken ct = default);
    public Task ClearAsync(CancellationToken ct = default);

    public Task<IReadOnlyList<SearchHit<TMeta>>> SearchAsync(
        string query, SearchQueryOptions? options = null, CancellationToken ct = default);

    public Task OptimizeAsync(CancellationToken ct = default);   // FTS 'optimize' + PRAGMA optimize + wal_checkpoint
    public Task RebuildAsync(CancellationToken ct = default);    // drop & recreate FTS tables, reindex from documents

    public ValueTask DisposeAsync();   // wal_checkpoint(TRUNCATE) then close
}

public sealed record SearchEntry<TMeta>(
    string Id,                 // REQUIRED, non-empty, caller-stable (e.g. file path or GUID)
    string Content,            // text to index (may be empty)
    TMeta Metadata,            // returned with hits
    string? Language = null);  // optional explicit ISO 639-1 code; null => detector/default

public sealed record SearchHit<TMeta>(
    string Id,
    TMeta Metadata,
    double Score,              // normalized 0..1, higher = better
    string? Snippet = null);   // populated only if StoreContent = true AND options.IncludeSnippet
```

### Options

```csharp
public sealed class ReperioOptions<TMeta>
{
    public IAnalyzerProvider Analyzers { get; }            // language packs register here
    public ILanguageDetector? LanguageDetector { get; set; }
    public string? DefaultLanguage { get; set; }           // fallback ISO code when detection off/uncertain
    public IFuzzyRanker FuzzyRanker { get; set; }          // default: TokenSetFuzzyRanker (FuzzySharp)

    public bool StoreContent { get; set; } = true;         // store one copy of Content (enables snippets + full fuzzy)
    public bool EnableTrigram { get; set; } = true;
    public bool EnableStemming { get; set; } = true;
    public bool EnablePhonetic { get; set; } = true;
    public bool RemoveStopWords { get; set; } = false;     // off by default (see §14.4)
    public int  MaxContentChars { get; set; } = 0;         // 0 = unbounded; else truncate indexed text

    public required JsonTypeInfo<TMeta> MetadataTypeInfo { get; set; }  // REQUIRED (AOT-safe), see §2/§14.2
}

public sealed class SearchQueryOptions
{
    public int    Limit { get; set; } = 50;
    public int    Offset { get; set; } = 0;
    public double MinScore { get; set; } = 0.0;
    public bool   EnableFuzzy { get; set; } = true;
    public bool   EnablePhonetic { get; set; } = true;
    public string? Language { get; set; }
    public bool   IncludeSnippet { get; set; } = false;    // requires StoreContent = true
    public int    CandidatePoolSize { get; set; } = 300;
    public TermMatch TermMatch { get; set; } = TermMatch.AllTerms;  // see §9.5
    public SnippetOptions Snippet { get; set; } = new();
}

public enum TermMatch { AllTerms, AnyTerms }   // multi-token base-term combination, see §9.5

public sealed class SnippetOptions
{
    public int    MaxLength { get; set; } = 200;
    public string StartMarker { get; set; } = "<mark>";
    public string EndMarker { get; set; } = "</mark>";
}
```

### Index profiles (presets)

Two named presets in core (`ReperioProfiles`, extension methods on `ReperioOptions<TMeta>`) encode
the benchmark-derived layout recommendations (`benchmarks/RESULTS.md`):

```csharp
public static ReperioOptions<TMeta> UseDesktopProfile<TMeta>(this ReperioOptions<TMeta> o);
// EnableTrigram=true, StoreContent=true, EnablePhonetic=true, RemoveStopWords=false, MaxContentChars=0
// (= the option defaults, made explicit/chainable)

public static ReperioOptions<TMeta> UseMobileProfile<TMeta>(this ReperioOptions<TMeta> o);
// EnableTrigram=false, StoreContent=true, EnablePhonetic=true, RemoveStopWords=true, MaxContentChars=4000
```

Rationale: dropping the trigram index (≈ half the database) improves size, query latency and
indexing throughput together, losing only mid-word substring search; `StoreContent` stays on
because §15.4 keeps the same text in `rank_text` when content is off (no size win, snippets lost);
phonetic stays on (cheap, useful for variants); stop-word removal trims common-term cost in the
stem/phonetic streams; `MaxContentChars` is the only lever below the rank_text floor (4000 is a
tunable starting default). The mobile profile keeps typo tolerance (fuzzy), stemming, phonetic
variants and the short-query prefix aid. The preset flags are persisted layout flags (§5): a
profile switch on an existing database triggers the mismatch-throw — `RebuildAsync()` applies the
new layout.

### Abstractions (namespace `ReperioNet.Abstractions`)

```csharp
public interface IStemmer        { string Stem(string token); }
public interface IPhoneticEncoder{ string? Encode(string token); }   // null => not encodable
public interface IStopWordFilter { bool IsStopWord(string token); }

public interface ILanguageAnalyzer
{
    string LanguageCode { get; }                 // ISO 639-1
    IStemmer Stemmer { get; }
    IPhoneticEncoder? Phonetic { get; }
    IStopWordFilter? StopWords { get; }
}

public interface IAnalyzerProvider
{
    void Register(ILanguageAnalyzer analyzer);   // last registration for a code wins
    ILanguageAnalyzer? Get(string languageCode);
    ILanguageAnalyzer Fallback { get; }          // identity analyzer: base tokens only
}

public interface ILanguageDetector { string? Detect(string text); }  // ISO 639-1 or null

public interface IFuzzyRanker { double Score(string query, string candidateText); }  // 0..1
```

### Intended usage

```csharp
var index = await SearchIndex<DocMeta>.OpenAsync("index.db", o =>
{
    o.MetadataTypeInfo   = AppJsonContext.Default.DocMeta;   // source-gen, required
    o.AddAllEuropeanLanguages();                              // ReperioNet.Languages.All
    o.LanguageDetector   = new NTextCatDetector();            // ReperioNet.LanguageDetection
});

await index.AddAsync(new(id: doc.Path, content: text,
                         metadata: new DocMeta { Path = doc.Path, FileName = name }));

var hits = await index.SearchAsync("müler rechnng");          // typo-tolerant, multilingual
foreach (var h in hits) Console.WriteLine($"{h.Score:F2}  {h.Metadata.FileName}");
```

---

## 5. Data Model (SQLite schema)

Minimum SQLite version: **3.43.0** (for `contentless_delete=1`). `OpenAsync` MUST: (a) verify `sqlite_version() >= 3.43.0` and throw `ReperioException` if older; (b) verify the FTS5 module by creating a temp FTS5 table and throw if unavailable.

```sql
CREATE TABLE IF NOT EXISTS reperio_meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS documents (
    rowid     INTEGER PRIMARY KEY,   -- internal; reused on update so FTS rows stay aligned
    doc_id    TEXT NOT NULL UNIQUE,  -- caller-provided stable id
    language  TEXT,                  -- resolved ISO code or NULL
    metadata  TEXT NOT NULL,         -- JSON (TMeta)
    rank_text TEXT NOT NULL,         -- normalized base token stream (used for fuzzy re-rank)
    content   TEXT                   -- original content; NULL unless StoreContent = true
);

CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(
    base, stem, phonetic,
    content='',
    contentless_delete=1,
    tokenize='unicode61 remove_diacritics 2'
);

-- created only when EnableTrigram = true
CREATE VIRTUAL TABLE IF NOT EXISTS documents_trgm USING fts5(
    text,
    content='',
    contentless_delete=1,
    tokenize='trigram'
);
```

`documents_fts.rowid` and `documents_trgm.rowid` MUST equal `documents.rowid` for the same document.

`reperio_meta` MUST persist: `schema_version` (start at `"1"`), and the layout-affecting flags `store_content`, `enable_trigram`, `enable_stemming`, `enable_phonetic`, `remove_stop_words`, `tokenizer` (the fixed `unicode61 remove_diacritics 2`). On reopen, if any persisted layout flag differs from the requested options, `OpenAsync` MUST throw `ReperioException` naming the mismatched flag and instructing the caller to call `RebuildAsync()` or open with matching options. No silent rebuild. See §14.7.

---

## 6. Indexing Pipeline

For each entry (single, or within one batch transaction):

1. **Validate** `Id` non-empty (else `ArgumentException`). `Content` may be empty.
2. **Apply `MaxContentChars`**: if > 0, truncate `Content` to that many characters before processing.
3. **Resolve language:** `entry.Language ?? LanguageDetector?.Detect(content) ?? DefaultLanguage` (may be null).
4. **Pick analyzer:** `Analyzers.Get(language) ?? Analyzers.Fallback`.
5. **Tokenize** `Content` (Unicode-aware: split on non-letter/non-digit, lowercase, fold diacritics consistent with `unicode61 remove_diacritics 2`). Produce three space-joined streams:
   - `base`     = all normalized tokens.
   - `stem`     = if `EnableStemming`: each token → `Stemmer.Stem`, deduped; else empty. If `RemoveStopWords` and `StopWords != null`, drop stop words from this stream.
   - `phonetic` = if `EnablePhonetic && Phonetic != null`: each token → `Encode` (skip nulls), deduped; else empty. Stop-word removal applies here too when enabled.
   - `rank_text` = `base`.
6. **Upsert (single writer, §8):**
   - Begin transaction (one transaction for an entire `AddRangeAsync`).
   - Look up existing `rowid` for `doc_id`. If found: reuse it; `DELETE` the existing `documents_fts` and (if enabled) `documents_trgm` rows for that rowid.
   - `INSERT OR REPLACE INTO documents(...)` with the resolved language, `metadata` JSON (serialized via `MetadataTypeInfo`), `rank_text`, and `content` (the truncated original if `StoreContent`, else NULL). Preserve the reused rowid.
   - `INSERT INTO documents_fts(rowid, base, stem, phonetic)`.
   - If trigram enabled: `INSERT INTO documents_trgm(rowid, text)` with `text = base` (normalized; see §14.3).
   - Commit.
   - Reuse prepared `SqliteCommand`s across a batch.

---

## 7. Language Packs & Extensibility

Each pack provides exactly one `ILanguageAnalyzer` and one explicit registration extension method. **No reflection / auto-discovery** (AOT requirement).

```csharp
namespace ReperioNet.Languages.De;
public static class GermanLanguageExtensions
{
    public static ReperioOptions<TMeta> AddGerman<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new GermanAnalyzer());   // "de": SnowballGermanStemmer + KoelnerPhonetik + de stop words
        return o;
    }
}
```

```csharp
namespace ReperioNet.Languages.All;
public static class AllLanguagesExtensions
{
    public static ReperioOptions<TMeta> AddAllEuropeanLanguages<TMeta>(this ReperioOptions<TMeta> o)
        => o.AddGerman().AddEnglish().AddFrench().AddSpanish().AddItalian().AddPortuguese()
            .AddDutch().AddSwedish().AddNorwegian().AddDanish().AddFinnish().AddRussian()
            .AddHungarian().AddRomanian().AddTurkish();   // full set
}
```

**Phonetic coverage (fixed):** ship `KoelnerPhonetik` for `de`, `DoubleMetaphone` for `en`. All other packs: `Phonetic == null` (stemmer + stop words only). Phonetic encoders MUST NOT be invented for languages where none is standard.

**Stop words:** each pack ships a curated stop-word list for its language (used only when `RemoveStopWords = true`).

**Fallback analyzer** (in core): `Stem` returns the token unchanged, `Phonetic == null`, `StopWords == null`. Used for unknown/undetected languages so search still works on `base`.

---

## 8. Concurrency, Async & SQLite Configuration

- **Single writer:** all writes (`Add`, `AddRange`, `Remove`, `Clear`, `Rebuild`, `Optimize`) are serialized through one `SemaphoreSlim(1,1)` over a dedicated, long-lived write connection. Consumers never see `SQLITE_BUSY`.
- **Reads** use separate short-lived connections (pooling left on) and may run concurrently with each other and, under WAL, with the writer.
- **Async model:** public methods are async and run the synchronous SQLite work on a background thread (`Task.Run`). This is acknowledged sync-over-async; its value is UI-thread offload plus the write gate.
- **PRAGMAs on every opened connection:**
  ```sql
  PRAGMA journal_mode = WAL;
  PRAGMA synchronous  = NORMAL;
  PRAGMA busy_timeout = 5000;
  PRAGMA foreign_keys = ON;
  PRAGMA temp_store   = MEMORY;
  ```
- `OptimizeAsync`: runs `INSERT INTO documents_fts(documents_fts) VALUES('optimize');` (and the trigram equivalent if enabled), then `PRAGMA optimize;` and `PRAGMA wal_checkpoint(TRUNCATE);`.
- `DisposeAsync`: `PRAGMA wal_checkpoint(TRUNCATE);` then close all connections and dispose the semaphore.
- XML docs MUST warn that the database file belongs on **local storage** (WAL is unsafe on SMB/NFS). The library takes the path as given and does not police it.
- Documented constraint: **one `SearchIndex<TMeta>` instance per database file per process.**

---

## 9. Query Pipeline (fully specified)

1. If `query` is null/whitespace → return empty list.
2. **Resolve query language:** `options.Language ?? LanguageDetector?.Detect(query) ?? DefaultLanguage`; pick analyzer (or fallback).
3. **Tokenize/normalize** the query identically to content → `qBase[]`, `qStem[]` (if stemming), `qPhon[]` (if phonetic and encoder present).
4. **Escape every token** for FTS5: replace `"` with `""` and wrap the token in double quotes. The MATCH expression is built ONLY from escaped tokens — never interpolate raw input.
5. **Build the `documents_fts` MATCH expression**, OR-combining column-scoped clauses that are non-empty:
   - `{base : "t1" OR "t2" OR ...}`
   - `OR {stem : "s1" OR ...}`  (if stemming)
   - `OR {phonetic : "p1" OR ...}`  (if `options.EnablePhonetic` and codes exist)
   - **Short-query aid:** if the whole `query` length < 3, append a prefix term on the last base token: `OR {base : "tN"*}` (FTS5 prefix query) to keep 1–2 char queries useful.
   - **Term combination (`TermMatch`, default `AllTerms`):** for multi-token queries the default is a two-pass scheme. A **strict pass** runs first with an implicit-AND base clause only — `base : ("t1" "t2" ...)` — requiring every base term; this is the common user intent and far cheaper than OR because the intersection is small (FTS5 bm25-scores every matching row). If the strict pass yields **fewer candidate rowids than `Limit`**, a **fallback pass** runs the full OR expression above (base OR'd, plus stem/phonetic clauses); its candidates are appended after the strict-pass candidates, deduplicated by rowid, and rank strictly behind them in the final ordering (tier before score in §9.12). Stem/phonetic clauses and the §9.6 trigram recall always keep OR/substring semantics — requiring all of them would over-restrict variant and typo matching. `TermMatch.AnyTerms` skips the strict pass and reproduces the plain OR expression. Single-token queries are unaffected.
6. **Trigram recall:** if `EnableTrigram` and `query.Length >= 3`, also run `documents_trgm MATCH :q` where `:q` is the escaped full query string (substring + typo recall, incidental CJK).
7. **Gather candidates:** run both MATCH queries selecting `rowid` and `bm25(<table>)`. Merge by `rowid`, keeping the **best (lowest) bm25** seen for that rowid. Keep the top `CandidatePoolSize` rowids ordered by bm25 (lowest first).
8. **Load** for each candidate: `metadata`, `rank_text`, and `content` (if stored).
9. **Normalize bm25** across the pool: let `bMin`/`bMax` be the min/max bm25 in the pool. `normBm25 = (bMax - b) / (bMax - bMin)` if `bMax > bMin`, else `1.0`. (bm25 is lower-is-better, so this maps the best match to 1.0.)
10. **Fuzzy pass** (if `options.EnableFuzzy`): `fuzzy01 = FuzzyRanker.Score(query, text)` where `text = content ?? rank_text`. The default `IFuzzyRanker` is `Fuzz.TokenSetRatio(query, text) / 100.0`.
11. **Final score:**
    - If `EnableFuzzy`: `score = 0.6 * fuzzy01 + 0.4 * normBm25`.
    - Else: `score = normBm25`.
    - **Exact-match boost:** if the diacritic-folded, lowercased `text` contains the diacritic-folded, lowercased raw `query` as a substring: `score = min(1.0, score + 0.15)`.
12. **Filter & page:** drop `score < MinScore`; order by `score` desc, then by `doc_id` asc as a stable tiebreaker (when the `AllTerms` strict pass ran, all-terms candidates order before fallback candidates — tier precedes score; see §9.5); apply `Offset`, then `Limit`.
13. **Project to `SearchHit`:** deserialize `metadata` via `MetadataTypeInfo`. If `IncludeSnippet && StoreContent`: build a snippet from `content` — a window of up to `Snippet.MaxLength` characters centered on the first occurrence (diacritic/case-insensitive) of any `qBase` token, wrapping each matched token occurrence in `StartMarker`/`EndMarker`. If no token is found in `content`, return the first `MaxLength` characters with no markers. If `IncludeSnippet` is requested while `StoreContent = false`, `Snippet` is null (no throw).

---

## 10. Edge Cases & Error Handling

- `ReperioException` (custom) is thrown for: missing FTS5 module; SQLite < 3.43.0; corrupt/incompatible schema; layout-flag mismatch on reopen; null `MetadataTypeInfo`.
- `ArgumentException` for empty `Id`.
- Empty/whitespace query → empty result (no throw).
- Query length 1–2 → trigram skipped; prefix aid applied (§9.5).
- Unknown/undetected language → identity fallback (base field only).
- `AddAsync` with existing `Id` → upsert preserving rowid (no duplicate hits).
- `RemoveAsync` unknown id → returns false.
- Special characters / FTS5 operators (`"`, `*`, `(`, `:`, `OR`, `NEAR`) in input → neutralized by token escaping (§9.4); treated as literal text.
- `MaxContentChars > 0` truncates indexed text (and stored content) consistently.
- Re-`OpenAsync` on the same file within a process while another instance is live is unsupported; documented.

---

## 11. Testing Requirements

Framework: **xUnit**. Tests MUST cover:
- CRUD: add, upsert (same id updates; rowid stable; no duplicate hits), remove, clear, count, contains.
- Base search correctness and bm25-influenced ordering.
- **Typo tolerance** (`"rechnng"` → `"Rechnung"`).
- **Substring** via trigram (`"chnun"` → `"Rechnung"`).
- **Stemming** per language (de: `"Rechnungen"` matches `"Rechnung"`; en: `"running"` matches `"run"`).
- **Phonetic** (de: `"Mueller"` matches `"Müller"`; en DoubleMetaphone case).
- **Multilingual mixed corpus** (de + en + fr in one index): detection + per-language stemming both work.
- **Unknown language** falls back gracefully.
- **Escaping**: queries with `"`, `*`, `(`, `OR`, `:` neither throw nor misbehave.
- **Concurrency**: many parallel `AddAsync` + concurrent `SearchAsync` → no corruption, no surfaced `SQLITE_BUSY`.
- **Batch**: 10k docs via one `AddRangeAsync` completes in a single transaction (perf sanity).
- **Schema versioning**: read/write; reopen with mismatched layout flag throws; `RebuildAsync` recovers.
- **StoreContent = false**: search works, fuzzy uses `rank_text`, snippets are null.
- **Scoring**: exact-match boost applies; `MinScore` filters; ordering deterministic via tiebreaker.
- **AOT/trimming smoke**: trimmed `dotnet publish` of the console sample runs a query with zero trim warnings; the MAUI sample builds for iOS and Android.

---

## 12. Packaging & CI

- **License: MIT.** Each package ships: README, description, tags (`search`, `fts5`, `sqlite`, `fuzzy`, `full-text-search`, `multilingual`, `embedded`, `cross-platform`), repository URL, `snupkg` symbols, deterministic build, SourceLink.
- **SemVer**; core and language packs versioned in lockstep for 1.x. Language packs reference core with a `[major.minor,*)`-style range matching the release.
- The `All` meta-package references every language pack; `LanguageDetection` references core.
- **GitHub Actions** (`ci.yml`): restore, build, run tests on ubuntu/windows/macos, run the trimmed-publish AOT smoke, pack all packages, and `dotnet nuget push` on a version tag.

---

## 13. Implementation Order (milestones)

1. **Core scaffolding**: project, `OpenAsync`, PRAGMAs, schema creation, `reperio_meta` versioning + flag checks, SQLite-version + FTS5 checks, `ReperioException`, required `MetadataTypeInfo`.
2. **CRUD on `base` only** (no fuzzy/stem/phonetic): Add/AddRange/Remove/Clear/Count/Contains + single-writer semaphore + transactions + JSON metadata round-trip. Working base search with bm25 ordering. `DisposeAsync`, `OptimizeAsync`, `RebuildAsync`.
3. **Trigram** table + substring/typo recall + candidate merge (§9.6–9.7).
4. **Fuzzy re-ranking** (`TokenSetFuzzyRanker`) + bm25 normalization + score blend + boost + `MinScore` + paging + snippets.
5. **Analyzer abstractions** + identity fallback + `IAnalyzerProvider` + language resolution; populate `stem`/`phonetic` columns; `RemoveStopWords` + `MaxContentChars`.
6. **Language packs `De` and `En`** end-to-end (Snowball + Kölner Phonetik / DoubleMetaphone + stop words) with tests; then the full European set.
7. **`ReperioNet.Languages.All`** meta + **`ReperioNet.LanguageDetection`** (NTextCat).
8. **AOT/trimming hardening**, samples (console + MAUI), CI, README/docs.

---

## 14. Design Decisions (rationale; all fixed)

1. **net8.0 only, no netstandard2.0** — avoids span/JSON-source-gen polyfills and keeps the trimming/AOT path simple; the target consumers (incl. MAUI mobile) are net8.0-based.
2. **`MetadataTypeInfo` required, no reflection JSON fallback** — reflection-based `System.Text.Json` is unsafe under full trimming/iOS AOT. Forcing a source-generated `JsonTypeInfo` guarantees the library is AOT-clean rather than failing at runtime on device.
3. **Trigram indexes the normalized `base` stream, not raw content** — smaller index and consistent with how queries are normalized (diacritic-folded), so substring matching behaves predictably.
4. **Stop-word removal OFF by default** — removing stop words can break exact/phrase intent and is risky across mixed-language corpora; index-size cost is acceptable. Opt-in via `RemoveStopWords`, and it only ever strips the `stem`/`phonetic` streams, never `base`.
5. **`MaxContentChars` default 0 (unbounded)** — least surprising for a general library; callers indexing very large blobs opt into a cap.
6. **Snowball stemmers vendored per language pack** — no dependency on an unmaintained third-party NuGet, full control over trimming/AOT, and the stemmers are tiny and self-contained.
7. **Reopen with mismatched layout flags throws (no silent rebuild)** — auto-rebuild risks silent, long, blocking data operations; the caller gets a clear error and an explicit `RebuildAsync()`.
8. **Fuzzy default = `Fuzz.TokenSetRatio/100`** — handles word-order and partial token overlap well for typo-tolerant document search; runs only on the bounded candidate pool against `content`/`rank_text`.
9. **Score blend `0.6*fuzzy + 0.4*bm25` with `+0.15` exact-substring boost** — fuzzy similarity drives perceived relevance for messy queries while bm25 preserves term-frequency signal; exact matches are nudged to the top. Deterministic and simple to reason about.
10. **Single-writer semaphore over a dedicated connection; reads on short-lived pooled connections** — encapsulates SQLite's single-writer rule so consumers never handle `SQLITE_BUSY`, while WAL still allows concurrent reads.

---

## 15. Implementation Notes — Milestones 1–2 (binding)

These notes are **binding** and **refine §5, §6 and §9 for Milestones 1–2**. Where they differ from earlier prose, this section wins. They exist to remove three subtle ambiguities that otherwise cause "search returns nothing" or rowid-drift bugs.

### 15.1 Connection string & connection management
- Build the connection string with `SqliteConnectionStringBuilder`:
  - `DataSource = databasePath`
  - `Mode = SqliteOpenMode.ReadWriteCreate`
  - `Cache = SqliteCacheMode.Default` (private cache; do **not** use shared cache)
  - `Pooling = true`
- Open **one dedicated write connection** in `OpenAsync`; keep it for the lifetime of the `SearchIndex`. All writes run on it under the `SemaphoreSlim(1,1)`.
- Each `SearchAsync`/`ContainsAsync`/`CountAsync` opens a **new** `SqliteConnection` (pooling reuses it), runs, and disposes.
- **Apply the per-connection PRAGMAs on every opened connection** (write and each read), immediately after `Open()`, because pooled connections may come back with reset state:
  ```sql
  PRAGMA journal_mode = WAL;
  PRAGMA synchronous  = NORMAL;
  PRAGMA busy_timeout = 5000;
  PRAGMA foreign_keys = ON;
  PRAGMA temp_store   = MEMORY;
  ```

### 15.2 `OpenAsync` startup order (binding)
1. Open write connection, apply PRAGMAs.
2. Verify version: parse `SELECT sqlite_version();`; throw `ReperioException` if `< 3.43.0`.
3. Verify FTS5: in try/catch run `CREATE VIRTUAL TABLE temp.__fts5check USING fts5(x); DROP TABLE temp.__fts5check;`; throw `ReperioException` if it fails.
4. If `reperio_meta` exists: read layout flags; throw `ReperioException` on mismatch (§5). Else create schema (respecting `EnableTrigram`) and write `schema_version="1"` + flags.
5. Require `MetadataTypeInfo` non-null (else throw).

### 15.3 `base` tokenization rule (binding — prevents folding mismatches)
- Write the **raw `Content`** (after `MaxContentChars` truncation) **directly** into `documents_fts.base`. Do **not** pre-normalize it in C#. The `unicode61 remove_diacritics 2` tokenizer owns folding/splitting for `base`, identically at index time and query time.
- The C# `Tokenize` helper is used **only** to (a) split the query into terms for the MATCH expression and (b) later feed the stemmer/phonetic encoder. It is **not** used to produce the `base` content.
- `Tokenize(string)` contract: enumerate Unicode runes; split on any rune where `Rune.IsLetterOrDigit` is false; `ToLowerInvariant()` each token; **do not strip diacritics in C#** (FTS5 does it on both sides). Return the non-empty tokens in order.

### 15.4 Column values per insert (binding)
For an indexed document, with `text` = `Content` truncated to `MaxContentChars` (or full if 0):
- `documents.content`   = `text` if `StoreContent` else `NULL`.
- `documents.rank_text` = `""` (empty string) if `StoreContent` else `text`. (Fuzzy, added later, reads `content` when present, else `rank_text`. No duplicate full-text storage.)
- `documents.metadata`  = `JsonSerializer.Serialize(meta, MetadataTypeInfo)`.
- `documents.language`  = resolved ISO code or `NULL`.
- `documents_fts.base`  = `text`.
- `documents_fts.stem`  = `""` in M1–2 (populated in M5).
- `documents_fts.phonetic` = `""` in M1–2 (populated in M5).
- `documents_trgm.text` = `text`, only if trigram enabled (trigram itself is M3; the column/table may exist from schema creation, but rows are written once trigram is in scope).

### 15.5 Upsert SQL (binding — two-step, keeps rowid stable)
Run inside a transaction (one transaction for an entire `AddRangeAsync`):

```sql
-- Step 1: find existing internal rowid (NULL if new)
SELECT rowid FROM documents WHERE doc_id = @doc_id;
```

If a row exists (reuse @rowid):
```sql
DELETE FROM documents_fts  WHERE rowid = @rowid;
DELETE FROM documents_trgm WHERE rowid = @rowid;   -- only if trigram enabled
UPDATE documents
   SET language = @language, metadata = @metadata,
       rank_text = @rank_text, content = @content
 WHERE rowid = @rowid;
```

If new:
```sql
INSERT INTO documents (doc_id, language, metadata, rank_text, content)
VALUES (@doc_id, @language, @metadata, @rank_text, @content);
-- then @rowid = last_insert_rowid()
```

Then (both cases) insert the FTS rows with the resolved @rowid:
```sql
INSERT INTO documents_fts (rowid, base, stem, phonetic)
VALUES (@rowid, @base, @stem, @phonetic);

INSERT INTO documents_trgm (rowid, text)           -- only if trigram enabled
VALUES (@rowid, @base);
```

Do **not** use `INSERT OR REPLACE` on `documents` (it can change the rowid and desync the FTS rows). The DELETE-then-INSERT on the contentless FTS tables requires `contentless_delete=1` (already mandated).

### 15.6 Milestone-2 `SearchAsync` (base-only) SQL (binding)
Build `@match` from escaped query tokens (§9.4), scoped to the `base` column:
- `@match = 'base : ("t1" OR "t2" OR ...)'` where each `tN` is the escaped, quoted token. If the query yields no tokens → return empty.

```sql
SELECT d.doc_id, d.metadata, bm25(documents_fts) AS rank
FROM documents_fts f
JOIN documents d ON d.rowid = f.rowid
WHERE documents_fts MATCH @match
ORDER BY rank            -- bm25 is lower-is-better
LIMIT @limit OFFSET @offset;
```

Mapping to `SearchHit` in M2: deserialize `metadata` via `MetadataTypeInfo`; `Snippet = null`; `Score` = normalized bm25 over the returned page using the §9.9 formula (best row → 1.0). The full candidate-pool + fuzzy blend (§9.7–9.11) arrives in M3–M4 and supersedes this simple scoring.

### 15.7 Verify-as-you-go
Build and run tests after Milestone 1 (open/create/reopen, version & FTS5 checks, flag-mismatch throw) before starting Milestone 2 (CRUD + base search). Do not proceed to M3 until M1–2 tests are green.

---

*End of PRD.*