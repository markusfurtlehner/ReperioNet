using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Sv;

/// <summary>
/// Pure managed port of the Snowball Swedish stemming algorithm
/// (<see href="https://snowballstem.org/algorithms/swedish/stemmer.html"/>).
/// Implements R1 with the minimum-three-letter adjustment, step 1 (the ending list plus the
/// valid s-ending rule for -s and the valid et-ending rule for -et/-ets), step 2 (consonant
/// pairs) and step 3 (lig/ig/els deletion, -öst → -ös and -fullt → -full).
/// </summary>
/// <remarks>The stemmer keeps no per-call state and is safe for concurrent use.</remarks>
public sealed class SnowballSwedishStemmer : IStemmer
{
    // Step 1 endings, longest first; the whole suffix must lie in R1. "s" and "et" carry extra conditions.
    private static readonly string[] MainSuffixes =
    [
        "heterna",
        "hetens",
        "arnas", "ernas", "ornas", "anden", "andes", "andet", "arens", "heten", "heter",
        "arna", "erna", "orna", "ande", "arne", "aste", "aren", "ades", "erns",
        "ade", "are", "ern", "ens", "het", "ast",
        "ad", "en", "ar", "er", "or", "as", "es", "at", "et",
        "a", "e", "s",
    ];

    // Step 3 endings, longest first; the whole suffix must lie in R1.
    private static readonly string[] OtherSuffixes = ["fullt", "els", "lig", "öst", "ig"];

    // Endings that disqualify a preceding -et/-ets from removal (frihet, societet, paket, komet, ...).
    private static readonly string[] EtExceptions =
    [
        "h", "iet", "uit", "fab", "cit", "dit", "alit", "ilit", "mit", "nit", "pit",
        "rit", "sit", "tit", "ivit", "kvit", "xit", "kom", "rak", "pak", "stak",
    ];

    /// <summary>Returns the Swedish Snowball stem of <paramref name="token"/>.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return token;
        }

        var buffer = token.ToCharArray();
        var length = buffer.Length;
        var p1 = MarkR1(buffer, length);

        length = MainSuffix(buffer, length, p1);
        length = ConsonantPair(buffer, length, p1);
        length = OtherSuffix(buffer, length, p1);

        return length == buffer.Length ? token : new string(buffer, 0, length);
    }

    private static bool IsVowel(char c)
        => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'ä' or 'å' or 'ö';

    private static bool IsValidSEnding(char c)
        => c is 'b' or 'c' or 'd' or 'f' or 'g' or 'h' or 'j' or 'k' or 'l' or 'm'
            or 'n' or 'o' or 'p' or 'r' or 't' or 'v' or 'y';

    private static bool IsValidOstEnding(char c)
        => c is 'i' or 'k' or 'l' or 'n' or 'p' or 'r' or 't' or 'u' or 'v';

    // True when buffer[..end] ends with the given suffix.
    private static bool EndsWith(char[] buffer, int end, string suffix)
    {
        if (end < suffix.Length)
        {
            return false;
        }

        var offset = end - suffix.Length;
        for (var i = 0; i < suffix.Length; i++)
        {
            if (buffer[offset + i] != suffix[i])
            {
                return false;
            }
        }

        return true;
    }

    // R1 per the Snowball definition, adjusted so that at least 3 letters precede it.
    private static int MarkR1(char[] buffer, int length)
    {
        if (length < 3)
        {
            return length;
        }

        var cursor = 0;
        while (cursor < length && !IsVowel(buffer[cursor]))
        {
            cursor++;
        }

        if (cursor >= length)
        {
            return length;
        }

        cursor++;
        while (cursor < length && IsVowel(buffer[cursor]))
        {
            cursor++;
        }

        if (cursor >= length)
        {
            return length;
        }

        cursor++;
        return cursor < 3 ? 3 : cursor;
    }

    // Valid et-ending: at least one letter, then a vowel, then a non-vowel preceding the suffix
    // at `position`, and none of the exception endings immediately before it.
    private static bool IsValidEtEnding(char[] buffer, int position)
    {
        if (position < 3 || IsVowel(buffer[position - 1]) || !IsVowel(buffer[position - 2]))
        {
            return false;
        }

        foreach (var exception in EtExceptions)
        {
            if (EndsWith(buffer, position, exception))
            {
                return false;
            }
        }

        return true;
    }

    // Step 1: delete the longest matching ending in R1 (with the -s/-ets/-et conditions).
    private static int MainSuffix(char[] buffer, int length, int p1)
    {
        foreach (var suffix in MainSuffixes)
        {
            if (!EndsWith(buffer, length, suffix) || length - suffix.Length < p1)
            {
                continue;
            }

            if (suffix == "s")
            {
                // -ets is removed as a whole when the valid et-ending holds; otherwise -s needs
                // a valid s-ending. The et/ets context may reach outside R1.
                if (length >= 3 && buffer[length - 3] == 'e' && buffer[length - 2] == 't'
                    && IsValidEtEnding(buffer, length - 3))
                {
                    return length - 3;
                }

                return length >= 2 && IsValidSEnding(buffer[length - 2]) ? length - 1 : length;
            }

            if (suffix == "et")
            {
                return IsValidEtEnding(buffer, length - 2) ? length - 2 : length;
            }

            return length - suffix.Length;
        }

        return length;
    }

    // Step 2: if the word ends dd, gd, nn, dt, gt, kt or tt in R1, remove the last letter.
    private static int ConsonantPair(char[] buffer, int length, int p1)
    {
        if (length >= 2 && length - 2 >= p1)
        {
            var first = buffer[length - 2];
            var last = buffer[length - 1];
            if ((last == 'd' && (first == 'd' || first == 'g'))
                || (last == 'n' && first == 'n')
                || (last == 't' && (first == 'd' || first == 'g' || first == 'k' || first == 't')))
            {
                return length - 1;
            }
        }

        return length;
    }

    // Step 3: lig/ig/els are deleted, -öst becomes -ös after a valid öst-ending, -fullt becomes -full.
    private static int OtherSuffix(char[] buffer, int length, int p1)
    {
        foreach (var suffix in OtherSuffixes)
        {
            var start = length - suffix.Length;
            if (!EndsWith(buffer, length, suffix) || start < p1)
            {
                continue;
            }

            if (suffix == "fullt")
            {
                return length - 1;
            }

            if (suffix == "öst")
            {
                return start >= 1 && IsValidOstEnding(buffer[start - 1]) ? length - 1 : length;
            }

            return start;
        }

        return length;
    }
}
