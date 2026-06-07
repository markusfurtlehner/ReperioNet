namespace ReperioNet.Languages.Fr;

/// <summary>Registration extensions for the French language pack.</summary>
public static class FrenchLanguageExtensions
{
    /// <summary>Registers the French (<c>"fr"</c>) analyzer with the index options.</summary>
    /// <typeparam name="TMeta">The metadata type of the index.</typeparam>
    /// <param name="o">The options to register the analyzer with.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddFrench<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new FrenchAnalyzer());
        return o;
    }
}
