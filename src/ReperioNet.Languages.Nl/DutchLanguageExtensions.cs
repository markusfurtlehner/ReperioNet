namespace ReperioNet.Languages.Nl;

/// <summary>Registration extensions for the Dutch language pack.</summary>
public static class DutchLanguageExtensions
{
    /// <summary>Registers the Dutch (<c>"nl"</c>) analyzer on <paramref name="o"/>.</summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options to register the analyzer on.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddDutch<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new DutchAnalyzer());
        return o;
    }
}
