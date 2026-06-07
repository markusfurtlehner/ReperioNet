using ReperioNet.Abstractions;

namespace ReperioNet.Languages.No;

/// <summary>
/// Pure managed port of the Snowball Norwegian (Bokmål) stemming algorithm
/// (<see href="https://snowballstem.org/algorithms/norwegian/stemmer.html"/>).
/// Implements R1 with the minimum-three-letter adjustment, step 1 (the ending list with the
/// valid s-ending rule, the protected -ers contexts and erte/ert → er), step 2 (dt/vt → drop
/// the t) and step 3 (leg/eleg/ig/eig/lig/elig/els/lov/elov/slov/hetslov deletion).
/// </summary>
/// <remarks>The stemmer keeps no per-call state and is safe for concurrent use.</remarks>
public sealed class SnowballNorwegianStemmer : IStemmer
{
    // Step 1 endings, longest first; the whole suffix must lie in R1.
    // "ers", "s", "erte" and "ert" carry special handling.
    private static readonly string[] MainSuffixes =
    [
        "hetenes",
        "hetene", "hetens",
        "endes", "heten", "heter",
        "ande", "ende", "edes", "enes", "erte",
        "ane", "ast", "ede", "ene", "ens", "ers", "ert", "ets", "het",
        "ar", "as", "en", "er", "es", "et",
        "a", "e", "s",
    ];

    // Step 3 endings, longest first; the whole suffix must lie in R1.
    private static readonly string[] OtherSuffixes =
    [
        "hetslov",
        "eleg", "elig", "elov", "slov",
        "eig", "els", "leg", "lig", "lov",
        "ig",
    ];

    /// <summary>Returns the Norwegian Snowball stem of <paramref name="token"/>.</summary>
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

        if (length >= 1 && buffer[length - 1] == '\'')
        {
            length--;
        }

        return length == buffer.Length ? token : new string(buffer, 0, length);
    }

    private static bool IsVowel(char c)
        => c is 'a' or 'e' or 'ê' or 'i' or 'o' or 'ò' or 'ó' or 'ô' or 'u' or 'y'
            or 'æ' or 'å' or 'ø';

    private static bool IsValidSEnding(char c)
        => c is 'b' or 'c' or 'd' or 'f' or 'g' or 'h' or 'j' or 'l' or 'm' or 'n'
            or 'o' or 'p' or 't' or 'v' or 'y' or 'z';

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

    // Step 1: delete the longest matching ending in R1 (with the -ers/-s/erte/ert special cases).
    private static int MainSuffix(char[] buffer, int length, int p1)
    {
        foreach (var suffix in MainSuffixes)
        {
            if (!EndsWith(buffer, length, suffix) || length - suffix.Length < p1)
            {
                continue;
            }

            if (suffix == "ers")
            {
                return ApplyErsSuffix(buffer, length);
            }

            if (suffix == "s")
            {
                return ApplySSuffix(buffer, length);
            }

            if (suffix == "erte" || suffix == "ert")
            {
                // Replace by "er": the suffix starts with those letters, so just truncate.
                return length - suffix.Length + 2;
            }

            return length - suffix.Length;
        }

        return length;
    }

    // -ers is deleted unless the preceding context protects it; the longest matching context
    // decides (Snowball among semantics), so giv/hav/skap delete while v/kap and the other
    // listed contexts keep the suffix.
    private static int ApplyErsSuffix(char[] buffer, int length)
    {
        var end = length - 3;
        if (EndsWith(buffer, end, "skap"))
        {
            return end;
        }

        if (EndsWith(buffer, end, "giv") || EndsWith(buffer, end, "hav"))
        {
            return end;
        }

        if (EndsWith(buffer, end, "amm") || EndsWith(buffer, end, "ast") || EndsWith(buffer, end, "ind")
            || EndsWith(buffer, end, "kap") || EndsWith(buffer, end, "omm") || EndsWith(buffer, end, "øst")
            || EndsWith(buffer, end, "kk") || EndsWith(buffer, end, "lt") || EndsWith(buffer, end, "nk")
            || EndsWith(buffer, end, "pp") || EndsWith(buffer, end, "v"))
        {
            return length;
        }

        return end;
    }

    // -s is deleted after a valid s-ending, after r not preceded by e, or after k preceded by a non-vowel.
    private static int ApplySSuffix(char[] buffer, int length)
    {
        if (length < 2)
        {
            return length;
        }

        var preceding = buffer[length - 2];
        var valid = IsValidSEnding(preceding)
            || (preceding == 'r' && (length < 3 || buffer[length - 3] != 'e'))
            || (preceding == 'k' && length >= 3 && !IsVowel(buffer[length - 3]));
        return valid ? length - 1 : length;
    }

    // Step 2: if the word ends dt or vt in R1, remove the final t.
    private static int ConsonantPair(char[] buffer, int length, int p1)
    {
        if (length >= 2 && length - 2 >= p1 && buffer[length - 1] == 't'
            && (buffer[length - 2] == 'd' || buffer[length - 2] == 'v'))
        {
            return length - 1;
        }

        return length;
    }

    // Step 3: delete the longest matching ending in R1.
    private static int OtherSuffix(char[] buffer, int length, int p1)
    {
        foreach (var suffix in OtherSuffixes)
        {
            var start = length - suffix.Length;
            if (EndsWith(buffer, length, suffix) && start >= p1)
            {
                return start;
            }
        }

        return length;
    }
}
