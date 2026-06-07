using ReperioNet.Abstractions;
using Xunit;

namespace ReperioNet.Tests;

/// <summary>End-to-end stem/phonetic/stop-word behavior through the core pipeline (M5; PRD §6.4–6.5, §9.2–9.5).</summary>
public class AnalyzerSearchTests
{
    private sealed class RecordingDetector(string? result) : ILanguageDetector
    {
        public List<string> Calls { get; } = [];

        public string? Detect(string text)
        {
            Calls.Add(text);
            return result;
        }
    }

    [Fact]
    public async Task Stemming_MatchesInflectedForms_BothDirections()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false; // isolate the stem column from substring recall
            o.Analyzers.Register(TestAnalyzerFactory.GermanSuffixStripper());
            o.DefaultLanguage = "de";
        });

        await index.AddRangeAsync(
        [
            TestOptions.Entry("plural", "Die Rechnungen liegen bei", language: "de"),
            TestOptions.Entry("singular", "Die Rechnung liegt bei", language: "de"),
        ]);

        // "Rechnungen" stems to "rechnung", matching both the singular and the plural document.
        Assert.Equal(2, (await index.SearchAsync("Rechnungen")).Count);
        Assert.Equal(2, (await index.SearchAsync("Rechnung")).Count);
    }

    [Fact]
    public async Task Stemming_Disabled_NoInflectionMatch()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.EnableStemming = false;
            o.Analyzers.Register(TestAnalyzerFactory.GermanSuffixStripper());
            o.DefaultLanguage = "de";
        });

        await index.AddAsync(TestOptions.Entry("singular", "Die Rechnung liegt bei", language: "de"));

        Assert.Empty(await index.SearchAsync("Rechnungen"));
        Assert.Single(await index.SearchAsync("Rechnung"));
    }

    [Fact]
    public async Task QueryLanguage_SelectsTheMatchingAnalyzer()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.Analyzers.Register(TestAnalyzerFactory.GermanSuffixStripper());
            o.Analyzers.Register(new StubAnalyzer("en", stem: t => t is "running" or "runs" ? "run" : t));
        });

        await index.AddAsync(TestOptions.Entry("doc", "running shoes", language: "en"));

        // The English stemmer reduces "runs" -> "run", matching the indexed stem of "running".
        Assert.Single(await index.SearchAsync("runs", new SearchQueryOptions { Language = "en" }));

        // The German analyzer leaves "runs" untouched: no stem match.
        Assert.Empty(await index.SearchAsync("runs", new SearchQueryOptions { Language = "de" }));
    }

    [Fact]
    public async Task QueryLanguage_ResolvedViaDetector_WhenNotSpecified()
    {
        using var db = new TestDatabase();
        var detector = new RecordingDetector("de");
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.Analyzers.Register(TestAnalyzerFactory.GermanSuffixStripper());
            o.LanguageDetector = detector;
        });

        // Explicit entry language: the detector must not run at index time.
        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnung liegt bei", language: "de"));
        Assert.Empty(detector.Calls);

        // No options.Language: the detector resolves the query language (§9.2).
        var hits = await index.SearchAsync("Rechnungen");
        Assert.Single(hits);
        Assert.Equal(["Rechnungen"], detector.Calls);

        // Explicit options.Language wins: no further detector call.
        await index.SearchAsync("Rechnungen", new SearchQueryOptions { Language = "de" });
        Assert.Single(detector.Calls);
    }

    [Fact]
    public async Task Phonetic_MatchesAcrossSpellingVariants()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.Analyzers.Register(TestAnalyzerFactory.VowelStripPhonetic());
        });

        await index.AddAsync(TestOptions.Entry("doc", "Herr Mueller", language: "de"));

        // "Müller" and "Mueller" share the vowel-stripped code "mllr": only the phonetic column hits.
        Assert.Single(await index.SearchAsync("Müller", new SearchQueryOptions { Language = "de" }));
    }

    [Fact]
    public async Task Phonetic_QueryOptionOff_DisablesPhoneticClause()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.Analyzers.Register(TestAnalyzerFactory.VowelStripPhonetic());
        });

        await index.AddAsync(TestOptions.Entry("doc", "Herr Mueller", language: "de"));

        var hits = await index.SearchAsync(
            "Müller",
            new SearchQueryOptions { Language = "de", EnablePhonetic = false });

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Phonetic_IndexOptionOff_ColumnStaysEmpty()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.EnablePhonetic = false;
            o.Analyzers.Register(TestAnalyzerFactory.VowelStripPhonetic());
        });

        await index.AddAsync(TestOptions.Entry("doc", "Herr Mueller", language: "de"));

        Assert.Empty(await index.SearchAsync("Müller", new SearchQueryOptions { Language = "de" }));
    }

    [Fact]
    public async Task RemoveStopWords_StripsStemStream_NeverBase()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.RemoveStopWords = true;
            o.Analyzers.Register(new StubAnalyzer(
                "de",
                stem: t => t.EndsWith("s", StringComparison.Ordinal) ? t[..^1] : t,
                stopWords: "und"));
            o.DefaultLanguage = "de";
        });

        await index.AddAsync(TestOptions.Entry("doc", "und kafka", language: "de"));

        // base is never stripped (§14.4): the literal stop word still matches.
        Assert.Single(await index.SearchAsync("und"));

        // "unds" stems to "und", which was dropped from the indexed stem stream: no hit.
        Assert.Empty(await index.SearchAsync("unds"));
    }

    [Fact]
    public async Task RemoveStopWordsOff_StemStreamKeepsStopWords()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.Analyzers.Register(new StubAnalyzer(
                "de",
                stem: t => t.EndsWith("s", StringComparison.Ordinal) ? t[..^1] : t,
                stopWords: "und"));
            o.DefaultLanguage = "de";
        });

        await index.AddAsync(TestOptions.Entry("doc", "und kafka", language: "de"));

        // With the flag off the stem stream contains "und", so the stemmed query matches.
        Assert.Single(await index.SearchAsync("unds"));
    }

    [Fact]
    public async Task UnknownLanguage_FallsBackToIdentityAnalyzer()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.Analyzers.Register(TestAnalyzerFactory.GermanSuffixStripper());
        });

        await index.AddAsync(TestOptions.Entry("doc", "contenu spécial", language: "fr"));

        // No "fr" analyzer registered: the identity fallback still indexes and matches base tokens.
        Assert.Single(await index.SearchAsync("contenu", new SearchQueryOptions { Language = "fr" }));
    }

    [Fact]
    public async Task Rebuild_RecomputesStemAndPhoneticStreams()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.Analyzers.Register(new StubAnalyzer(
                "de",
                stem: t => t.EndsWith("en", StringComparison.Ordinal) ? t[..^2] : t,
                phonetic: t =>
                {
                    var encoded = new string(t.Where(c => !"aeiouäöü".Contains(c)).ToArray());
                    return encoded.Length == 0 ? null : encoded;
                }));
        });

        await index.AddRangeAsync(
        [
            TestOptions.Entry("stems", "Die Rechnungen liegen bei", language: "de"),
            TestOptions.Entry("phon", "Herr Mueller", language: "de"),
        ]);

        await index.RebuildAsync();

        // Both derived streams must survive the rebuild (recomputed from stored language + text).
        Assert.Contains(await index.SearchAsync("Rechnung", new SearchQueryOptions { Language = "de" }), h => h.Id == "stems");
        Assert.Contains(await index.SearchAsync("Müller", new SearchQueryOptions { Language = "de" }), h => h.Id == "phon");
    }

    [Fact]
    public async Task Upsert_RefreshesDerivedStreams()
    {
        using var db = new TestDatabase();
        await using var index = await TestOptions.OpenAsync(db, o =>
        {
            o.EnableTrigram = false;
            o.Analyzers.Register(TestAnalyzerFactory.GermanSuffixStripper());
            o.DefaultLanguage = "de";
        });

        await index.AddAsync(TestOptions.Entry("doc", "Die Rechnungen liegen bei", language: "de"));
        await index.AddAsync(TestOptions.Entry("doc", "Etwas ganz anderes", language: "de"));

        Assert.Empty(await index.SearchAsync("Rechnung"));
    }
}
