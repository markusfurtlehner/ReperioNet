namespace ReperioNet;

/// <summary>A single search result.</summary>
/// <typeparam name="TMeta">The metadata type stored in the index.</typeparam>
/// <param name="Id">The caller-provided identifier of the matched document.</param>
/// <param name="Metadata">The metadata payload stored with the document.</param>
/// <param name="Score">Normalized relevance in the range 0..1; higher is better.</param>
/// <param name="Snippet">A highlighted excerpt; populated only if <c>ReperioOptions&lt;TMeta&gt;.StoreContent</c> is <see langword="true"/> and <see cref="SearchQueryOptions.IncludeSnippet"/> was requested.</param>
public sealed record SearchHit<TMeta>(
    string Id,
    TMeta Metadata,
    double Score,
    string? Snippet = null);
