namespace ReperioNet.Languages.Ro;

/// <summary>Registration extensions for the Romanian language pack.</summary>
public static class RomanianLanguageExtensions
{
    /// <summary>Registers the Romanian (<c>"ro"</c>) analyzer with the index options.</summary>
    /// <typeparam name="TMeta">The metadata type of the index.</typeparam>
    /// <param name="o">The options to register the analyzer with.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddRomanian<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.Analyzers.Register(new RomanianAnalyzer());
        return o;
    }
}
