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

    /// <summary>Snippet generation options.</summary>
    public SnippetOptions Snippet { get; set; } = new();
}
