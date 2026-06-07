using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Da;

/// <summary>
/// Curated Danish stop-word list (articles, pronouns, prepositions, conjunctions and common
/// auxiliary verb forms), applied only when <c>ReperioOptions&lt;TMeta&gt;.RemoveStopWords</c> is enabled.
/// </summary>
public sealed class DanishStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "og", "i", "jeg", "det", "at", "en", "den", "til", "er", "som",
        "på", "de", "med", "han", "af", "for", "ikke", "der", "var", "mig",
        "sig", "men", "et", "har", "om", "vi", "min", "havde", "ham", "hun",
        "nu", "over", "da", "fra", "du", "ud", "sin", "dem", "os", "op",
        "man", "hans", "hvor", "eller", "hvad", "skal", "selv", "her", "alle", "vil",
        "blev", "kunne", "ind", "når", "være", "dog", "noget", "ville", "jo", "deres",
        "efter", "ned", "skulle", "denne", "end", "dette", "mit", "også", "under", "have",
        "dig", "anden", "hende", "mine", "alt", "meget", "sit", "sine", "vor", "mod",
        "disse", "hvis", "din", "nogle", "hos", "blive", "mange", "ad", "bliver", "hendes",
        "været", "thi", "jer", "sådan",
    };

    /// <summary>Returns <see langword="true"/> if <paramref name="token"/> is a Danish stop word.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    public bool IsStopWord(string token) => !string.IsNullOrEmpty(token) && Words.Contains(token);
}
