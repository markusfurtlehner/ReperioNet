namespace ReperioNet.Languages.It;

/// <summary>Registration extensions for the Italian language pack.</summary>
public static class ItalianLanguageExtensions
{
    /// <summary>Registers the Italian (<c>"it"</c>) analyzer with the index options.</summary>
    /// <typeparam name="TMeta">The metadata type of the index.</typeparam>
    /// <param name="o">The options to register the analyzer with.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddItalian<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new ItalianAnalyzer());
        return o;
    }
}
