using ReperioNet.Abstractions;

namespace ReperioNet.Languages.No;

/// <summary>
/// Curated Norwegian stop-word list (articles, pronouns, prepositions, conjunctions and common
/// auxiliary verb forms, including frequent Nynorsk variants), applied only when
/// <c>ReperioOptions&lt;TMeta&gt;.RemoveStopWords</c> is enabled.
/// </summary>
public sealed class NorwegianStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "og", "i", "jeg", "det", "at", "en", "et", "den", "til", "er",
        "som", "på", "de", "med", "han", "av", "ikke", "ikkje", "der", "så",
        "var", "meg", "seg", "men", "ett", "har", "om", "vi", "min", "mitt",
        "ha", "hadde", "hun", "nå", "over", "da", "ved", "fra", "du", "ut",
        "sin", "dem", "oss", "opp", "man", "kan", "hans", "hvor", "eller", "hva",
        "skal", "selv", "sjøl", "her", "alle", "vil", "bli", "ble", "blei", "blitt",
        "kunne", "inn", "når", "være", "kom", "noen", "noe", "ville", "dere", "deres",
        "kun", "ja", "etter", "ned", "skulle", "denne", "for", "deg", "si", "sine",
        "sitt", "mot", "å", "meget", "hvorfor", "dette", "disse", "uten", "hvordan", "ingen",
        "din", "ditt", "blir", "samme", "hvilken", "hvilke", "sånn", "inni", "mellom", "vår",
        "hver", "hvem", "hvis", "både", "bare", "enn", "fordi", "før", "mange", "også",
        "slik", "vært", "begge", "siden", "dei", "deira", "deim", "eg", "ein", "eit",
        "elles", "ho", "henne", "hennes", "hennar", "honom", "hjå", "korleis", "kva", "kven",
        "kvifor", "me", "medan", "mi", "mine", "mykje", "no", "nokon", "noko", "nokre",
        "sidan", "so", "somme", "um", "upp", "vere", "vore", "verte", "vart",
    };

    /// <summary>Returns <see langword="true"/> if <paramref name="token"/> is a Norwegian stop word.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    public bool IsStopWord(string token) => !string.IsNullOrEmpty(token) && Words.Contains(token);
}
