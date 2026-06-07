using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Fi;

/// <summary>
/// Finnish stop-word filter; a curated subset of the official Snowball Finnish stop-word list
/// (forms of "olla", negation verbs, pronouns and conjunctions).
/// </summary>
public sealed class FinnishStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // Forms of olla (to be).
        "olla", "olen", "olet", "on", "olemme", "olette", "ovat", "ole", "oli",
        "olisi", "olisit", "olisin", "olisimme", "olisitte", "olisivat",
        "olit", "olin", "olimme", "olitte", "olivat", "ollut", "olleet",

        // Negation verb.
        "en", "et", "ei", "emme", "ette", "eivät",

        // Personal pronouns (common case forms).
        "minä", "minun", "minut", "minua", "minussa", "minusta", "minuun", "minulla", "minulta", "minulle",
        "sinä", "sinun", "sinut", "sinua", "sinussa", "sinusta", "sinuun", "sinulla", "sinulta", "sinulle",
        "hän", "hänen", "hänet", "häntä", "hänessä", "hänestä", "häneen", "hänellä", "häneltä", "hänelle",
        "me", "meidän", "meidät", "meitä", "meissä", "meistä", "meihin", "meillä", "meiltä", "meille",
        "te", "teidän", "teidät", "teitä", "teissä", "teistä", "teihin", "teillä", "teiltä", "teille",
        "he", "heidän", "heidät", "heitä", "heissä", "heistä", "heihin", "heillä", "heiltä", "heille",

        // Demonstratives.
        "tämä", "tämän", "tätä", "tässä", "tästä", "tähän", "tällä", "tältä", "tälle",
        "tuo", "tuon", "tuota", "tuosta", "tuohon", "tuolla",
        "se", "sen", "sitä", "siinä", "siitä", "siihen", "sillä", "siltä", "sille", "siksi",
        "nämä", "näiden", "näitä", "näissä", "näistä", "näihin", "näillä", "näiltä", "näille",
        "nuo", "ne", "niiden", "niitä", "niissä", "niistä", "niihin", "niillä", "niiltä", "niille",

        // Interrogatives.
        "kuka", "kenen", "mikä", "minkä", "mitä", "missä", "mistä", "mihin", "millä", "miksi", "mitkä",

        // Relatives.
        "joka", "jonka", "jota", "jossa", "josta", "johon", "jolla", "jolta", "jolle", "jotka",

        // Conjunctions and common particles.
        "että", "ja", "jos", "joten", "koska", "kuin", "mutta", "niin", "sekä",
        "tai", "vaan", "vai", "vaikka", "kun", "nyt", "itse",
    };

    /// <inheritdoc />
    public bool IsStopWord(string token) => Words.Contains(token);
}
