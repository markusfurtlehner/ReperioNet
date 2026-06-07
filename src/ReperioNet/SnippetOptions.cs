namespace ReperioNet;

/// <summary>Controls snippet generation when <see cref="SearchQueryOptions.IncludeSnippet"/> is enabled.</summary>
public sealed class SnippetOptions
{
    /// <summary>Maximum snippet length in characters. Default: 200.</summary>
    public int MaxLength { get; set; } = 200;

    /// <summary>Marker inserted before each matched token occurrence. Default: <c>"&lt;mark&gt;"</c>.</summary>
    public string StartMarker { get; set; } = "<mark>";

    /// <summary>Marker inserted after each matched token occurrence. Default: <c>"&lt;/mark&gt;"</c>.</summary>
    public string EndMarker { get; set; } = "</mark>";
}
