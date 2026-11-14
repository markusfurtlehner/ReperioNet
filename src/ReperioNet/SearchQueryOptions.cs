namespace ReperioNet;

/// <summary>Per-query options for <c>SearchIndex&lt;TMeta&gt;.SearchAsync</c>.</summary>
public sealed class SearchQueryOptions
{
    /// <summary>Maximum number of hits to return. Default: 50.</summary>
    public int Limit { get; set; } = 50;

    /// <summary>Number of hits to skip (paging). Default: 0.</summary>
    public int Offset { get; set; }

    /// <summary>Hits scoring below this threshold are dropped. Default: 0.0.</summary>
    public double MinScore { get; set; }

    /// <summary>
    /// How multiple query terms combine on the base column. Default: <see cref="ReperioNet.TermMatch.AllTerms"/>
    /// (all terms required, with an automatic any-term fallback when fewer than <see cref="Limit"/>
    /// candidates match; fallback hits rank after all-terms hits). Stem/phonetic variant matching
    /// and trigram substring recall are unaffected by this setting.
    /// </summary>
    public TermMatch TermMatch { get; set; } = TermMatch.AllTerms;

    /// <summary>Enables the fuzzy re-ranking pass. Default: <see langword="true"/>.</summary>
    public bool EnableFuzzy { get; set; } = true;

    /// <summary>Includes the phonetic column in the match expression (when the analyzer provides an encoder). Default: <see langword="true"/>.</summary>
    public bool EnablePhonetic { get; set; } = true;

    /// <summary>Explicit ISO 639-1 query language; <see langword="null"/> defers to the detector or default language.</summary>
    public string? Language { get; set; }

    /// <summary>Populates <see cref="SearchHit{TMeta}.Snippet"/>; requires <c>ReperioOptions&lt;TMeta&gt;.StoreContent</c>. Default: <see langword="false"/>.</summary>
    public bool IncludeSnippet { get; set; }

    /// <summary>Size of the candidate pool gathered before re-ranking. Default: 300.</summary>
    public int CandidatePoolSize { get; set; } = 300;

    /// <summary>
    /// How many of the best candidates (by term-match tier, then bm25) are scored against their stored text: fuzzy score, exact-match
    /// boost, <see cref="MinScore"/> and snippet. The rest of the pool is still returned, behind them within its tier, with the bm25
    /// share of the blend (0.4 x normalized bm25) as its score and no snippet; its text is never loaded. Default: <see langword="null"/>
    /// (the whole pool is scored). Set it when the pool is large and only the first hits need an exact rank: the cost of a search
    /// otherwise grows with the pool times the size of the stored texts.
    /// </summary>
    public int? ScorePoolSize { get; set; }

    /// <summary>Snippet generation options.</summary>
    public SnippetOptions Snippet { get; set; } = new();
}
