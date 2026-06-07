namespace ReperioNet.Languages.Da;

/// <summary>Registration extensions for the Danish language pack.</summary>
public static class DanishLanguageExtensions
{
    /// <summary>Registers the Danish (<c>"da"</c>) analyzer on <paramref name="o"/>.</summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options to register the analyzer on.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddDanish<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new DanishAnalyzer());
        return o;
    }
}
