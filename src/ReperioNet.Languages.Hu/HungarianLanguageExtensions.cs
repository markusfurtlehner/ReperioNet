namespace ReperioNet.Languages.Hu;

/// <summary>Registration extensions for the Hungarian language pack.</summary>
public static class HungarianLanguageExtensions
{
    /// <summary>Registers the Hungarian (<c>"hu"</c>) analyzer on <paramref name="o"/>.</summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options being configured.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddHungarian<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new HungarianAnalyzer());
        return o;
    }
}
