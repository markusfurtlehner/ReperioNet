namespace ReperioNet.Languages.Es;

/// <summary>Registration extensions for the Spanish language pack.</summary>
public static class SpanishLanguageExtensions
{
    /// <summary>Registers the Spanish (<c>"es"</c>) analyzer with the index options.</summary>
    /// <typeparam name="TMeta">The metadata type of the index.</typeparam>
    /// <param name="o">The options to register the analyzer with.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddSpanish<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new SpanishAnalyzer());
        return o;
    }
}
