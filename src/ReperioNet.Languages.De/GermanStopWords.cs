using ReperioNet.Abstractions;

namespace ReperioNet.Languages.De;

/// <summary>
/// Curated German stop-word list: articles, pronouns, prepositions, conjunctions, auxiliaries and
/// high-frequency particles. Entries are lowercase with diacritics preserved, matching the token
/// contract.
/// </summary>
/// <remarks>
/// Thread-safe: the set is built once and never mutated, so <see cref="IsStopWord"/> may be called
/// concurrently.
/// </remarks>
public sealed class GermanStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // Articles and determiners.
        "der", "die", "das", "den", "dem", "des",
        "ein", "eine", "einem", "einen", "einer", "eines",
        "kein", "keine", "keinem", "keinen", "keiner", "keines",
        "jede", "jedem", "jeden", "jeder", "jedes",
        "alle", "allem", "allen", "aller", "alles",
        "manche", "manchem", "manchen", "mancher", "manches",
        "solche", "solchem", "solchen", "solcher", "solches",
        "welche", "welchem", "welchen", "welcher", "welches",
        "dies", "diese", "diesem", "diesen", "dieser", "dieses",
        "jene", "jenem", "jenen", "jener", "jenes",
        "derselbe", "dieselbe", "dasselbe",

        // Personal, possessive and reflexive pronouns.
        "ich", "du", "er", "sie", "es", "wir", "ihr",
        "mich", "dich", "sich", "uns", "euch",
        "mir", "dir", "ihm", "ihn", "ihnen",
        "mein", "meine", "meinem", "meinen", "meiner", "meines",
        "dein", "deine", "deinem", "deinen", "deiner", "deines",
        "sein", "seine", "seinem", "seinen", "seiner", "seines",
        "ihre", "ihrem", "ihren", "ihrer", "ihres",
        "unser", "unsere", "unserem", "unseren", "unserer", "unseres",
        "euer", "eure", "eurem", "euren", "eurer", "eures",
        "man", "etwas", "nichts",

        // Prepositions and contractions.
        "an", "am", "auf", "aus", "bei", "beim", "bis", "durch", "für", "gegen",
        "hinter", "im", "in", "ins", "mit", "nach", "neben", "ohne", "seit",
        "über", "um", "unter", "vom", "von", "vor", "während", "wegen",
        "zwischen", "zu", "zum", "zur",

        // Conjunctions and connectives.
        "aber", "als", "also", "auch", "dass", "daß", "denn", "doch", "indem",
        "ob", "obwohl", "oder", "sondern", "sowie", "und", "weil", "wenn", "wie",
        "damit", "zwar",

        // Auxiliary and modal verb forms.
        "bin", "bist", "ist", "sind", "seid", "sei",
        "war", "warst", "waren", "wart", "gewesen",
        "werde", "wirst", "wird", "werden", "werdet",
        "wurde", "wurden", "würde", "würden",
        "habe", "hast", "hat", "habt", "haben",
        "hatte", "hattest", "hatten", "hattet",
        "kann", "kannst", "können", "könnt", "könnte",
        "muss", "musst", "müssen", "müsst", "musste",
        "soll", "sollst", "sollen", "sollte",
        "will", "willst", "wollen", "wollte",
        "darf", "dürfen", "mag", "mögen", "möchte",

        // High-frequency adverbs, particles and question words.
        "da", "dann", "dazu", "dort", "hier", "hin", "jetzt", "nun", "noch",
        "nur", "schon", "sehr", "selbst", "so", "sonst", "weg", "weiter",
        "wieder", "einmal", "nicht", "was", "wer", "wo", "wann", "warum",
    };

    /// <inheritdoc />
    public bool IsStopWord(string token) => Words.Contains(token);
}
