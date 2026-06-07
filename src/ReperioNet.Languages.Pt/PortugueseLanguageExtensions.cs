namespace ReperioNet.Languages.Pt;

/// <summary>Registration extensions for the Portuguese language pack.</summary>
public static class PortugueseLanguageExtensions
{
    /// <summary>Registers the Portuguese (<c>"pt"</c>) analyzer with the index options.</summary>
    /// <typeparam name="TMeta">The metadata type of the index.</typeparam>
    /// <param name="o">The options to register the analyzer with.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddPortuguese<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new PortugueseAnalyzer());
        return o;
    }
}
