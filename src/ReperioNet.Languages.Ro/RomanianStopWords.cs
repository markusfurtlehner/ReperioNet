using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Ro;

/// <summary>
/// Romanian stop-word filter: articles, pronouns, prepositions, conjunctions and the common
/// auxiliary forms of <c>a fi</c> and <c>a avea</c> (lowercase, diacritics preserved).
/// Entries containing ș/ț are listed in both the comma-below and legacy cedilla spellings.
/// </summary>
public sealed class RomanianStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // Articles, demonstratives, determiners.
        "un", "o", "una", "unui", "unei", "unor", "al", "a", "ai", "ale",
        "acest", "acesta", "această", "aceasta", "aceste", "acestea", "acestei",
        "acestor", "acestui", "acel", "acela", "acea", "aceea", "acele",
        "acelea", "alt", "alta", "altă", "alte", "alți", "alţi", "altul",
        "fiecare", "orice", "oricare", "toată", "toate", "tot", "toți", "toţi",
        "totul", "câțiva", "câţiva", "niște", "nişte",
        // Pronouns.
        "eu", "tu", "el", "ea", "noi", "voi", "ei", "ele", "mă", "te", "se",
        "ne", "vă", "îi", "îl", "îmi", "îți", "îţi", "le", "li", "lor", "lui",
        "mie", "mine", "tine", "sine", "care", "cărei", "căror", "cărui", "ce",
        "cine", "cineva", "ceva", "nimeni", "nimic",
        // Possessives.
        "meu", "mea", "mei", "mele", "tău", "ta", "tăi", "tale", "său", "sa",
        "săi", "sale", "nostru", "noastră", "noștri", "noştri", "noastre",
        "vostru", "voastră", "voștri", "voştri", "voastre",
        // Prepositions and conjunctions.
        "și", "şi", "sau", "ori", "dar", "iar", "însă", "ci", "că", "căci",
        "dacă", "deci", "deși", "deşi", "încât", "întrucât", "fiindcă",
        "deoarece", "de", "la", "în", "într", "dintr", "printr", "din", "prin",
        "pe", "cu", "fără", "despre", "după", "până", "peste", "spre", "sub",
        "lângă", "între", "asupra", "către", "pentru", "ca", "cum", "când",
        "unde", "cât", "câtă", "câte", "câți", "câţi", "nu", "da", "mai", "doar",
        "chiar", "încă", "așa", "aşa", "atât", "foarte", "prea", "abia", "aici",
        "acolo", "azi", "mâine", "ieri", "apoi",
        // a fi.
        "fi", "fie", "fii", "fiu", "fim", "fiți", "fiţi", "fiind", "fost",
        "sunt", "sînt", "suntem", "sîntem", "sunteți", "sunteţi", "sînteți",
        "ești", "eşti", "este", "e", "eram", "erai", "era", "erați", "eraţi",
        "erau", "voi", "vei", "va", "vom", "veți", "veţi", "vor",
        // a avea.
        "avea", "am", "are", "avem", "aveți", "aveţi", "au", "aș", "aş", "ar",
        "ași", "aşi", "ați", "aţi", "aveam", "avut", "având",
    };

    /// <inheritdoc />
    public bool IsStopWord(string token) => !string.IsNullOrEmpty(token) && Words.Contains(token);
}
