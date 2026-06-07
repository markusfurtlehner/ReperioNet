namespace ReperioNet.Languages.En;

/// <summary>Explicit registration of the English language pack (no reflection or auto-discovery).</summary>
public static class EnglishLanguageExtensions
{
    /// <summary>Registers the English ("en") analyzer: Snowball (Porter2) stemmer, Double Metaphone and English stop words.</summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options to register the analyzer on.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddEnglish<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new EnglishAnalyzer());
        return o;
    }
}
