using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Fr;

/// <summary>
/// French stop-word filter: articles, pronouns, prepositions, conjunctions and the common
/// auxiliary forms of <c>être</c> and <c>avoir</c> (lowercase, diacritics preserved).
/// </summary>
public sealed class FrenchStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // Articles, determiners, elided forms.
        "au", "aux", "le", "la", "les", "un", "une", "des", "du", "de",
        "c", "d", "j", "l", "m", "n", "qu", "s", "t",
        "ce", "cet", "cette", "ces", "ceci", "cela",
        // Pronouns.
        "elle", "elles", "il", "ils", "je", "tu", "nous", "vous", "on",
        "me", "te", "se", "moi", "toi", "soi", "lui", "leur", "eux", "y", "en",
        "qui", "que", "quoi", "dont", "où",
        // Possessives.
        "ma", "mon", "mes", "ta", "ton", "tes", "sa", "son", "ses",
        "notre", "nos", "votre", "vos", "leurs",
        // Prepositions and conjunctions.
        "à", "dans", "par", "pour", "sur", "sous", "avec", "sans", "chez",
        "entre", "vers", "et", "ou", "mais", "donc", "or", "ni", "car", "si",
        "comme", "quand", "ne", "pas", "plus", "même", "aussi", "très", "tout",
        "toute", "tous", "toutes",
        // être.
        "été", "étée", "étées", "étés", "étant", "suis", "es", "est", "sommes",
        "êtes", "sont", "serai", "seras", "sera", "serons", "serez", "seront",
        "serais", "serait", "serions", "seriez", "seraient", "étais", "était",
        "étions", "étiez", "étaient", "fus", "fut", "fûmes", "fûtes", "furent",
        "sois", "soit", "soyons", "soyez", "soient", "fusse", "fusses", "fût",
        "fussions", "fussiez", "fussent", "être",
        // avoir.
        "ayant", "eu", "eue", "eues", "eus", "ai", "as", "avons", "avez", "ont",
        "aurai", "auras", "aura", "aurons", "aurez", "auront", "aurais", "aurait",
        "aurions", "auriez", "auraient", "avais", "avait", "avions", "aviez",
        "avaient", "eut", "eûmes", "eûtes", "eurent", "aie", "aies", "ait",
        "ayons", "ayez", "aient", "eusse", "eusses", "eût", "eussions", "eussiez",
        "eussent", "avoir",
    };

    /// <inheritdoc />
    public bool IsStopWord(string token) => !string.IsNullOrEmpty(token) && Words.Contains(token);
}
