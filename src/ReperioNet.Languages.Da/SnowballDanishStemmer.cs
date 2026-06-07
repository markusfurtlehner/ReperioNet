using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Da;

/// <summary>
/// Pure managed port of the Snowball Danish stemming algorithm
/// (<see href="https://snowballstem.org/algorithms/danish/stemmer.html"/>).
/// Implements R1 with the minimum-three-letter adjustment, step 1 (the ending list with the
/// valid s-ending rule), step 2 (gd/dt/gt/kt → drop the last letter), step 3 (igst → drop st;
/// ig/lig/elig/els deletion with a step 2 re-check; løst → løs) and step 4 (undoubling of an
/// identical final consonant pair at the R1 boundary).
/// </summary>
/// <remarks>The stemmer keeps no per-call state and is safe for concurrent use.</remarks>
public sealed class SnowballDanishStemmer : IStemmer
{
    // Step 1 endings, longest first; the whole suffix must lie in R1. "s" carries the s-ending rule.
    private static readonly string[] MainSuffixes =
    [
        "erendes",
        "erende", "hedens",
        "ethed", "erede", "heden", "heder", "endes", "ernes", "erens", "erets",
        "ered", "ende", "erne", "eren", "erer", "heds", "enes", "eres", "eret",
        "hed", "ene", "ere", "ens", "ers", "ets",
        "en", "er", "es", "et",
        "e", "s",
    ];

    // Step 3 endings, longest first; the whole suffix must lie in R1.
    private static readonly string[] OtherSuffixes = ["elig", "løst", "lig", "els", "ig"];

    /// <summary>Returns the Danish Snowball stem of <paramref name="token"/>.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length < 3)
        {
            return token;
        }

        var buffer = token.ToCharArray();
        var length = buffer.Length;
        var p1 = MarkR1(buffer, length);

        length = MainSuffix(buffer, length, p1);
        length = ConsonantPair(buffer, length, p1);
        length = OtherSuffix(buffer, length, p1);
        length = Undouble(buffer, length, p1);

        if (length >= 1 && buffer[length - 1] == '\'')
        {
            length--;
        }

        return length == buffer.Length ? token : new string(buffer, 0, length);
    }

    private static bool IsVowel(char c)
        => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'æ' or 'å' or 'ø';

    private static bool IsValidSEnding(char c)
        => c is 'a' or 'b' or 'c' or 'd' or 'f' or 'g' or 'h' or 'j' or 'k' or 'l'
            or 'm' or 'n' or 'o' or 'p' or 'r' or 't' or 'v' or 'y' or 'z' or 'å' or '\'';

    // Consonants undoubled by step 4.
    private static bool IsUndoubleConsonant(char c)
        => c is 'b' or 'd' or 'f' or 'g' or 'k' or 'l' or 'm' or 'n' or 'p' or 'r' or 's' or 't';

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

    // R1 per the Snowball definition (started after a leading apostrophe for acronym loanwords
    // such as pc'en), adjusted so that at least 3 letters precede it. Callers guarantee length >= 3.
    private static int MarkR1(char[] buffer, int length)
    {
        var p1 = length;
        var apostrophe = -1;
        for (var i = 0; i < length; i++)
        {
            if (buffer[i] == '\'')
            {
                apostrophe = i;
                break;
            }
        }

        if (apostrophe >= 0)
        {
            p1 = apostrophe + 1;
        }
        else
        {
            var cursor = 0;
            while (cursor < length && !IsVowel(buffer[cursor]))
            {
                cursor++;
            }

            if (cursor < length)
            {
                cursor++;
                while (cursor < length && IsVowel(buffer[cursor]))
                {
                    cursor++;
                }

                if (cursor < length)
                {
                    p1 = cursor + 1;
                }
            }
        }

        return p1 < 3 ? 3 : p1;
    }

    // Step 1: delete the longest matching ending in R1 (-s only after a valid s-ending).
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
                return length >= 2 && IsValidSEnding(buffer[length - 2]) ? length - 1 : length;
            }

            return length - suffix.Length;
        }

        return length;
    }

    // Step 2: if the word ends gd, dt, gt or kt in R1, remove the last letter.
    private static int ConsonantPair(char[] buffer, int length, int p1)
    {
        if (length >= 2 && length - 2 >= p1)
        {
            var first = buffer[length - 2];
            var last = buffer[length - 1];
            if ((last == 'd' && first == 'g')
                || (last == 't' && (first == 'd' || first == 'g' || first == 'k')))
            {
                return length - 1;
            }
        }

        return length;
    }

    // Step 3: a final igst loses its st; then ig/lig/elig/els in R1 are deleted (followed by a
    // step 2 re-check) and løst in R1 becomes løs.
    private static int OtherSuffix(char[] buffer, int length, int p1)
    {
        if (EndsWith(buffer, length, "igst"))
        {
            length -= 2;
        }

        foreach (var suffix in OtherSuffixes)
        {
            var start = length - suffix.Length;
            if (!EndsWith(buffer, length, suffix) || start < p1)
            {
                continue;
            }

            if (suffix == "løst")
            {
                return length - 1;
            }

            return ConsonantPair(buffer, start, p1);
        }

        return length;
    }

    // Step 4: if the word ends with two identical consonants from the undoubling set and the
    // last one lies in R1, remove it.
    private static int Undouble(char[] buffer, int length, int p1)
    {
        if (length >= 2 && length - 1 >= p1 && IsUndoubleConsonant(buffer[length - 1])
            && buffer[length - 2] == buffer[length - 1])
        {
            return length - 1;
        }

        return length;
    }
}
