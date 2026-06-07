namespace ReperioNet.Languages.Ru;

/// <summary>Registration extensions for the Russian language pack.</summary>
public static class RussianLanguageExtensions
{
    /// <summary>Registers the Russian (<c>"ru"</c>) analyzer on <paramref name="o"/>.</summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options being configured.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddRussian<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new RussianAnalyzer());
        return o;
    }
}
