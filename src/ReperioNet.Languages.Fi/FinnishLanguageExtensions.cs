namespace ReperioNet.Languages.Fi;

/// <summary>Registration extensions for the Finnish language pack.</summary>
public static class FinnishLanguageExtensions
{
    /// <summary>Registers the Finnish (<c>"fi"</c>) analyzer on <paramref name="o"/>.</summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options being configured.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddFinnish<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new FinnishAnalyzer());
        return o;
    }
}
