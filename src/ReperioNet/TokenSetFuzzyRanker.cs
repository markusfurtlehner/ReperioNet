using System.Globalization;
using FuzzySharp;
using ReperioNet.Abstractions;

namespace ReperioNet;

/// <summary>
/// Default <see cref="IFuzzyRanker"/>: FuzzySharp's token-set ratio scaled to 0..1.
/// Handles word-order differences and partial token overlap well for typo-tolerant document search.
/// </summary>
public sealed class TokenSetFuzzyRanker : IFuzzyRanker
{
    /// <inheritdoc />
    public double Score(string query, string candidateText) => Known(query, candidateText) switch
    {
        Shortcut.EveryToken => 1.0,
        Shortcut.NoTokenInALongText => 0.0,
        _ => Fuzz.TokenSetRatio(query, candidateText) / 100.0,
    };

    /// <summary>What is known about the token-set ratio of a pair without computing it.</summary>
    internal enum Shortcut
    {
        /// <summary>Nothing: the ratio must be computed.</summary>
        None,

        /// <summary>The text holds every token of the query: the ratio is 100.</summary>
        EveryToken,

        /// <summary>The text holds no token of the query and is so much longer than the query that the ratio rounds to 0.</summary>
        NoTokenInALongText,
    }

    /// <summary>The most tokens of a query a shortcut is looked for.</summary>
    private const int MaxQueryTokens = 16;

    /// <summary>The slots of the table that notes which tokens of the text were counted already.</summary>
    private const int SeenSlots = 8192;

    /// <summary>
    /// Whether every token of <paramref name="query"/> is a token of <paramref name="text"/>, with the tokens the token-set ratio
    /// compares: what stands between white space, compared exactly (case and punctuation count).
    /// </summary>
    internal static bool ContainsEveryToken(string query, string text) => Known(query, text) == Shortcut.EveryToken;

    /// <summary>
    /// The two cases in which the token-set ratio is known from one pass over the text, without splitting, sorting and comparing
    /// it (2.6.2); that work was most of the time of scoring long texts, and it allocates several times the text.
    /// <para>
    /// The ratio compares the tokens both have (the intersection) with each side's sorted tokens. When the text holds every
    /// token of the query, the query's tokens are the intersection and the ratio is 100. When it holds none, the intersection is
    /// empty and what remains is the similarity of the query's tokens with all distinct tokens of the text, which is at most
    /// twice the shorter length over the sum of both; from 400 times the query's length on that is under half a percent, and the
    /// ratio, a whole number of percent, is 0.
    /// </para>
    /// </summary>
    internal static Shortcut Known(string query, string text)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
        {
            return Shortcut.None;
        }

        // The query's distinct tokens as ranges of the query, and their length when joined by single spaces.
        Span<(int Start, int Length)> wanted = stackalloc (int, int)[MaxQueryTokens];
        var count = 0;
        var queryLength = 0;
        for (var i = 0; i < query.Length;)
        {
            if (IsSeparator(query[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < query.Length && !IsSeparator(query[i]))
            {
                i++;
            }

            var token = query.AsSpan(start, i - start);
            var known = false;
            for (var k = 0; k < count && !known; k++)
            {
                known = token.SequenceEqual(query.AsSpan(wanted[k].Start, wanted[k].Length));
            }

            if (!known)
            {
                if (count == wanted.Length)
                {
                    return Shortcut.None;
                }

                wanted[count++] = (start, i - start);
                queryLength += token.Length + (count > 1 ? 1 : 0);
            }
        }

        if (count == 0)
        {
            return Shortcut.None;
        }

        // A lower bound of the length of the text's distinct tokens joined by single spaces: every token is counted the first
        // time its hash is seen. Two different tokens with one hash count once, which only lowers the bound. It is counted up
        // to the length from which the ratio is 0, and not at all for a query so long that the table could fill first.
        var longFrom = (long)queryLength * 400;
        var bounded = longFrom <= SeenSlots;
        Span<int> seen = bounded ? stackalloc int[SeenSlots] : default;
        seen.Clear();
        long distinctLength = 0;

        var tokens = count;
        var missing = count;
        for (var i = 0; i < text.Length;)
        {
            if (IsSeparator(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && !IsSeparator(text[i]))
            {
                i++;
            }

            var token = text.AsSpan(start, i - start);
            for (var k = 0; k < count; k++)
            {
                var (from, length) = wanted[k];
                if (length == token.Length && token.SequenceEqual(query.AsSpan(from, length)))
                {
                    // Found: its place is taken by the last one still missing.
                    wanted[k] = wanted[count - 1];
                    count--;
                    if (--missing == 0)
                    {
                        return Shortcut.EveryToken;
                    }

                    break;
                }
            }

            if (bounded && distinctLength <= longFrom)
            {
                var hash = string.GetHashCode(token);
                if (hash == 0)
                {
                    hash = 1;
                }

                var slot = hash & (SeenSlots - 1);
                while (seen[slot] != 0 && seen[slot] != hash)
                {
                    slot = (slot + 1) & (SeenSlots - 1);
                }

                if (seen[slot] == 0)
                {
                    seen[slot] = hash;
                    distinctLength += token.Length + (distinctLength > 0 ? 1 : 0);
                }
            }
        }

        return missing == tokens && bounded && distinctLength > longFrom ? Shortcut.NoTokenInALongText : Shortcut.None;
    }

    /// <summary>The characters the token-set ratio splits at: the white space of a regular expression.</summary>
    private static bool IsSeparator(char c) => c switch
    {
        ' ' or (char)9 or (char)10 or (char)11 or (char)12 or (char)13 or (char)0x85 => true,
        < (char)0x80 => false,
        _ => CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator,
    };
}
