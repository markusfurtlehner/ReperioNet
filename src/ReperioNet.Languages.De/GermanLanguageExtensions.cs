namespace ReperioNet.Languages.De;

/// <summary>Registers the German language pack on a <see cref="ReperioOptions{TMeta}"/>.</summary>
public static class GermanLanguageExtensions
{
    /// <summary>
    /// Registers the German ("de") analyzer — <see cref="SnowballGermanStemmer"/>,
    /// <see cref="KoelnerPhonetik"/> and the German stop words — on <paramref name="o"/>.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options to register the analyzer on.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddGerman<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new GermanAnalyzer());
        return o;
    }
}
