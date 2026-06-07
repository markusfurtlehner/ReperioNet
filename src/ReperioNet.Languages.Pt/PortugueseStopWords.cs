using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Pt;

/// <summary>
/// Portuguese stop-word filter: articles, pronouns, prepositions (including contractions),
/// conjunctions and the common auxiliary forms of <c>ser</c>, <c>estar</c>, <c>ter</c> and
/// <c>haver</c> (lowercase, diacritics preserved).
/// </summary>
public sealed class PortugueseStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // Articles and contractions.
        "o", "a", "os", "as", "um", "uma", "uns", "umas", "ao", "aos", "à", "às",
        "do", "da", "dos", "das", "no", "na", "nos", "nas", "num", "numa",
        "pelo", "pela", "pelos", "pelas", "dele", "dela", "deles", "delas",
        // Pronouns.
        "eu", "tu", "ele", "ela", "nós", "vós", "eles", "elas", "você", "vocês",
        "me", "te", "se", "lhe", "lhes", "vos", "mim", "ti", "si", "que", "quem",
        "qual", "cujo", "cuja",
        // Possessives and demonstratives.
        "meu", "minha", "meus", "minhas", "teu", "tua", "teus", "tuas", "seu",
        "sua", "seus", "suas", "nosso", "nossa", "nossos", "nossas", "vosso",
        "vossa", "vossos", "vossas", "este", "esta", "estes", "estas", "esse",
        "essa", "esses", "essas", "aquele", "aquela", "aqueles", "aquelas",
        "isto", "isso", "aquilo",
        // Prepositions and conjunctions.
        "de", "em", "por", "para", "com", "sem", "sob", "sobre", "entre", "até",
        "desde", "contra", "perante", "e", "ou", "nem", "mas", "porém", "porque",
        "como", "quando", "onde", "não", "sim", "mais", "menos", "muito", "muita",
        "muitos", "muitas", "pouco", "pouca", "poucos", "poucas", "também", "só",
        "já", "ainda", "depois", "antes", "mesmo", "mesma", "todo", "toda",
        "todos", "todas", "outro", "outra", "outros", "outras", "cada", "há",
        // ser.
        "ser", "sou", "és", "é", "somos", "sois", "são", "era", "eram", "éramos",
        "fui", "foi", "fomos", "foram", "fosse", "fossem", "seja", "sejam",
        "serei", "será", "seremos", "serão", "seria", "seriam", "sendo", "sido",
        // estar.
        "estar", "estou", "está", "estás", "estamos", "estão", "estava",
        "estavam", "estive", "esteve", "estivemos", "estiveram", "estando",
        "estado",
        // ter / haver.
        "ter", "tenho", "tens", "tem", "têm", "temos", "tinha", "tinham", "tive",
        "teve", "tivemos", "tiveram", "tendo", "tido", "haver", "hei", "havemos",
        "hão", "havia", "houve",
    };

    /// <inheritdoc />
    public bool IsStopWord(string token) => !string.IsNullOrEmpty(token) && Words.Contains(token);
}
