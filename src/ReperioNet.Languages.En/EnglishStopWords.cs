using ReperioNet.Abstractions;

namespace ReperioNet.Languages.En;

/// <summary>
/// Curated English stop-word list (function words: articles, pronouns, prepositions, conjunctions,
/// auxiliary verbs and similar). Used only when <c>ReperioOptions&lt;TMeta&gt;.RemoveStopWords</c> is
/// enabled. Stateless and safe for concurrent use.
/// </summary>
public sealed class EnglishStopWords : IStopWordFilter
{
    /// <summary>The stop words, all lowercase, matched ordinally.</summary>
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        // Articles and determiners.
        "a", "an", "the", "this", "that", "these", "those", "each", "every", "either", "neither",
        "some", "any", "no", "all", "both", "few", "more", "most", "much", "many", "other", "another",
        "such", "own", "same",

        // Pronouns.
        "i", "me", "my", "mine", "myself",
        "you", "your", "yours", "yourself", "yourselves",
        "he", "him", "his", "himself",
        "she", "her", "hers", "herself",
        "it", "its", "itself",
        "we", "us", "our", "ours", "ourselves",
        "they", "them", "their", "theirs", "themselves",
        "who", "whom", "whose", "which", "what",

        // Auxiliary and common verbs.
        "am", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "having",
        "do", "does", "did", "doing", "done",
        "will", "would", "shall", "should", "can", "could", "may", "might", "must",

        // Prepositions.
        "of", "in", "on", "at", "by", "for", "with", "about", "against", "between", "among",
        "into", "through", "during", "before", "after", "above", "below", "to", "from", "up",
        "down", "out", "off", "over", "under", "again", "further", "without", "within", "along",
        "across", "behind", "near", "upon", "since", "until",

        // Conjunctions and connectives.
        "and", "but", "or", "nor", "so", "yet", "if", "then", "else", "because", "as", "while",
        "when", "where", "why", "how", "although", "though", "unless", "whether",

        // Adverbs and particles.
        "not", "only", "just", "very", "too", "also", "here", "there", "once", "now", "ever",
        "never", "always", "often", "still", "even",

        // Common contractions (apostrophe-free, as produced by the tokenizer).
        "don", "doesn", "didn", "isn", "aren", "wasn", "weren", "hasn", "haven", "hadn",
        "won", "wouldn", "shouldn", "couldn", "ll", "re", "ve",
    };

    /// <summary>Returns <see langword="true"/> if <paramref name="token"/> is an English stop word.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    public bool IsStopWord(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Words.Contains(token);
    }
}
