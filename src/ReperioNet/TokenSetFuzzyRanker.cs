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
    public double Score(string query, string candidateText)
        => ContainsEveryToken(query, candidateText) ? 1.0 : Fuzz.TokenSetRatio(query, candidateText) / 100.0;

    /// <summary>
    /// Whether every token of <paramref name="query"/> is a token of <paramref name="text"/>, with the tokens the token-set ratio
    /// compares: what stands between white space, compared exactly (case and punctuation count). The ratio of such a pair is 100
    /// by its definition (the query's tokens are the intersection), so the text need not be split, sorted and compared (2.6.2):
    /// that was most of the time of scoring a long text for a word it holds.
    /// </summary>
    internal static bool ContainsEveryToken(string query, string text)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
        {
            return false;
        }

        // The query's distinct tokens as ranges of the query.
        Span<(int Start, int Length)> wanted = stackalloc (int, int)[16];
        var count = 0;
        for (var i = 0; i < query.Length;)
        {
            if (!IsTokenChar(query[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < query.Length && IsTokenChar(query[i]))
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
                    return false;
                }

                wanted[count++] = (start, i - start);
            }
        }

        if (count == 0)
        {
            return false;
        }

        var missing = count;
        for (var i = 0; i < text.Length;)
        {
            if (!IsTokenChar(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && IsTokenChar(text[i]))
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
                        return true;
                    }

                    break;
                }
            }
        }

        return false;
    }

    private static bool IsTokenChar(char c) => !char.IsWhiteSpace(c);
}
