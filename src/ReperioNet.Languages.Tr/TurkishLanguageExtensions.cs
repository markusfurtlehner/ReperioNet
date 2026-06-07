namespace ReperioNet.Languages.Tr;

/// <summary>Registration extensions for the Turkish language pack.</summary>
public static class TurkishLanguageExtensions
{
    /// <summary>Registers the Turkish (<c>"tr"</c>) analyzer on <paramref name="o"/>.</summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options being configured.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddTurkish<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new TurkishAnalyzer());
        return o;
    }
}
