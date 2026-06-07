using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Es;

/// <summary>
/// Spanish stop-word filter: articles, pronouns, prepositions, conjunctions and the common
/// auxiliary forms of <c>ser</c>, <c>estar</c> and <c>haber</c> (lowercase, diacritics preserved).
/// </summary>
public sealed class SpanishStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // Articles and determiners.
        "el", "la", "los", "las", "un", "una", "unos", "unas", "lo", "al", "del",
        "este", "esta", "estos", "estas", "ese", "esa", "esos", "esas",
        "aquel", "aquella", "aquellos", "aquellas", "esto", "eso", "aquello",
        // Pronouns.
        "yo", "tú", "él", "ella", "ello", "nosotros", "nosotras", "vosotros",
        "vosotras", "ellos", "ellas", "usted", "ustedes", "me", "te", "se", "nos",
        "os", "le", "les", "mí", "ti", "sí", "quien", "quienes", "que", "qué",
        "cual", "cuales", "cuyo", "cuya",
        // Possessives.
        "mi", "mis", "tu", "tus", "su", "sus", "mío", "mía", "míos", "mías",
        "tuyo", "tuya", "tuyos", "tuyas", "suyo", "suya", "suyos", "suyas",
        "nuestro", "nuestra", "nuestros", "nuestras", "vuestro", "vuestra",
        "vuestros", "vuestras",
        // Prepositions and conjunctions.
        "a", "ante", "bajo", "con", "contra", "de", "desde", "durante", "en",
        "entre", "hacia", "hasta", "para", "por", "según", "sin", "sobre", "tras",
        "y", "e", "o", "u", "ni", "pero", "sino", "porque", "como", "cuando",
        "donde", "si", "no", "más", "menos", "muy", "ya", "también", "tampoco",
        "todo", "toda", "todos", "todas", "otro", "otra", "otros", "otras",
        "mucho", "mucha", "muchos", "muchas", "poco", "poca", "pocos", "pocas",
        "tanto", "tanta", "algo", "nada", "cada", "tal", "hay",
        // ser.
        "ser", "soy", "eres", "es", "somos", "sois", "son", "sea", "seas",
        "seamos", "sean", "seré", "serás", "será", "seremos", "serán", "era",
        "eras", "éramos", "eran", "fui", "fuiste", "fue", "fuimos", "fueron",
        "fuera", "fueras", "fueran", "siendo", "sido",
        // estar.
        "estar", "estoy", "estás", "está", "estamos", "estáis", "están", "estaba",
        "estaban", "estado", "estando", "estuvo",
        // haber.
        "haber", "he", "has", "ha", "hemos", "habéis", "han", "haya", "hayan",
        "habrá", "había", "habían", "hube", "hubo", "habiendo", "habido",
    };

    /// <inheritdoc />
    public bool IsStopWord(string token) => !string.IsNullOrEmpty(token) && Words.Contains(token);
}
