using ReperioNet.Abstractions;

namespace ReperioNet.Languages.It;

/// <summary>
/// Italian stop-word filter: articles, pronouns, prepositions (including articulated forms),
/// conjunctions and the common auxiliary forms of <c>essere</c> and <c>avere</c>
/// (lowercase, diacritics preserved; elided remnants such as <c>l</c> and <c>un</c> included).
/// </summary>
public sealed class ItalianStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // Articles and articulated prepositions.
        "il", "lo", "la", "i", "gli", "le", "un", "uno", "una", "l",
        "ad", "al", "allo", "ai", "agli", "all", "agl", "alla", "alle",
        "con", "col", "coi", "da", "dal", "dallo", "dai", "dagli", "dall",
        "dagl", "dalla", "dalle", "di", "del", "dello", "dei", "degli", "dell",
        "degl", "della", "delle", "in", "nel", "nello", "nei", "negli", "nell",
        "negl", "nella", "nelle", "su", "sul", "sullo", "sui", "sugli", "sull",
        "sugl", "sulla", "sulle", "per", "tra", "fra", "contro",
        // Pronouns.
        "io", "tu", "lui", "lei", "noi", "voi", "loro", "mi", "ti", "ci", "vi",
        "li", "ne", "si", "che", "chi", "cui",
        // Possessives and demonstratives.
        "mio", "mia", "miei", "mie", "tuo", "tua", "tuoi", "tue", "suo", "sua",
        "suoi", "sue", "nostro", "nostra", "nostri", "nostre", "vostro", "vostra",
        "vostri", "vostre", "questo", "questi", "questa", "queste", "quello",
        "quelli", "quella", "quelle",
        // Conjunctions and particles.
        "e", "ed", "o", "ma", "se", "perché", "anche", "come", "dove", "dov",
        "non", "più", "quale", "quanto", "quanti", "quanta", "quante", "tutto",
        "tutti", "tutta", "tutte", "a", "c", "è", "sì",
        // avere.
        "avere", "ho", "hai", "ha", "abbiamo", "avete", "hanno", "abbia",
        "abbiate", "abbiano", "avrò", "avrai", "avrà", "avremo", "avrete",
        "avranno", "avrei", "avresti", "avrebbe", "avevo", "avevi", "aveva",
        "avevamo", "avevano", "ebbi", "ebbe", "ebbero", "avendo", "avuto",
        // essere.
        "essere", "sono", "sei", "siamo", "siete", "sia", "siate", "siano",
        "sarò", "sarai", "sarà", "saremo", "sarete", "saranno", "sarei",
        "saresti", "sarebbe", "ero", "eri", "era", "eravamo", "erano", "fui",
        "fosti", "fu", "fummo", "furono", "fossi", "fosse", "fossero", "essendo",
        "stato", "stata", "stati", "state",
    };

    /// <inheritdoc />
    public bool IsStopWord(string token) => !string.IsNullOrEmpty(token) && Words.Contains(token);
}
