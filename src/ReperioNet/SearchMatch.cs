namespace ReperioNet;

/// <summary>A search result without its metadata (see <c>SearchIndex&lt;TMeta&gt;.SearchMatchesAsync</c>).</summary>
/// <param name="Id">The caller-provided identifier of the matched document.</param>
/// <param name="Score">Normalized relevance in the range 0..1; higher is better.</param>
/// <param name="Snippet">A highlighted excerpt, under the same conditions as <c>SearchHit&lt;TMeta&gt;.Snippet</c>.</param>
public sealed record SearchMatch(string Id, double Score, string? Snippet = null);
