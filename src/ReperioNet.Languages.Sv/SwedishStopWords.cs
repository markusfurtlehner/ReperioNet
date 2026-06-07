using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Sv;

/// <summary>
/// Curated Swedish stop-word list (articles, pronouns, prepositions, conjunctions and common
/// auxiliary verb forms), applied only when <c>ReperioOptions&lt;TMeta&gt;.RemoveStopWords</c> is enabled.
/// </summary>
public sealed class SwedishStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "och", "det", "att", "i", "en", "jag", "hon", "som", "han", "på",
        "den", "med", "var", "sig", "för", "så", "till", "är", "men", "ett",
        "om", "hade", "de", "av", "icke", "mig", "du", "henne", "då", "sin",
        "nu", "har", "inte", "hans", "honom", "skulle", "hennes", "där", "min", "man",
        "ej", "vid", "kunde", "något", "från", "ut", "när", "efter", "upp", "vi",
        "dem", "vara", "vad", "över", "än", "dig", "kan", "sina", "här", "ha",
        "mot", "alla", "under", "någon", "eller", "allt", "mycket", "sedan", "ju", "denna",
        "själv", "detta", "åt", "utan", "varit", "hur", "ingen", "mitt", "ni", "bli",
        "blev", "oss", "din", "dessa", "några", "deras", "blir", "mina", "samma", "vilken",
        "er", "sådan", "vår", "blivit", "dess", "inom", "mellan", "sådant", "varför", "varje",
        "vilka", "ditt", "vem", "vilket", "sitta", "sådana", "vart", "dina", "vars", "vårt",
        "våra", "ert", "era", "vilkas",
    };

    /// <summary>Returns <see langword="true"/> if <paramref name="token"/> is a Swedish stop word.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    public bool IsStopWord(string token) => !string.IsNullOrEmpty(token) && Words.Contains(token);
}
