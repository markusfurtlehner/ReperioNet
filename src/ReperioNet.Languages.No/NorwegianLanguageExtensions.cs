namespace ReperioNet.Languages.No;

/// <summary>Registration extensions for the Norwegian language pack.</summary>
public static class NorwegianLanguageExtensions
{
    /// <summary>Registers the Norwegian (<c>"no"</c>) analyzer on <paramref name="o"/>.</summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options to register the analyzer on.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddNorwegian<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new NorwegianAnalyzer());
        return o;
    }
}
