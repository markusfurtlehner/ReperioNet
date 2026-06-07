namespace ReperioNet.Languages.Sv;

/// <summary>Registration extensions for the Swedish language pack.</summary>
public static class SwedishLanguageExtensions
{
    /// <summary>Registers the Swedish (<c>"sv"</c>) analyzer on <paramref name="o"/>.</summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options to register the analyzer on.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddSwedish<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new SwedishAnalyzer());
        return o;
    }
}
