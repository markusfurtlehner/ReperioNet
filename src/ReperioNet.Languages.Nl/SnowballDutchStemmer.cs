using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Nl;

/// <summary>
/// Pure managed port of the Snowball Dutch stemming algorithm
/// (<see href="https://snowballstem.org/algorithms/dutch/stemmer.html"/>).
/// Implements the prelude (umlaut/acute removal and consonantal y/i marking), the R1/R2 regions
/// with the minimum-three-letter R1 adjustment, suffix steps 1, 2, 3a and 3b, the final vowel
/// undoubling step and the postlude.
/// </summary>
/// <remarks>The stemmer keeps no per-call state and is safe for concurrent use.</remarks>
public sealed class SnowballDutchStemmer : IStemmer
{
    /// <summary>Returns the Dutch Snowball stem of <paramref name="token"/>.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return token;
        }

        var buffer = token.ToCharArray();
        var length = buffer.Length;

        Prelude(buffer, length);
        MarkRegions(buffer, length, out var p1, out var p2);

        length = Step1(buffer, length, p1);
        length = Step2(buffer, length, p1, out var eFound);
        length = Step3A(buffer, length, p1, p2);
        length = Step3B(buffer, length, p1, p2, eFound);
        length = Step4(buffer, length);

        Postlude(buffer, length);
        return new string(buffer, 0, length);
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'è';

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

    // Removes umlauts and acute accents, then marks initial y, y after a vowel and i between
    // vowels as consonants by upper-casing them (undone by the postlude).
    private static void Prelude(char[] buffer, int length)
    {
        for (var i = 0; i < length; i++)
        {
            buffer[i] = buffer[i] switch
            {
                'ä' or 'á' => 'a',
                'ë' or 'é' => 'e',
                'ï' or 'í' => 'i',
                'ö' or 'ó' => 'o',
                'ü' or 'ú' => 'u',
                var other => other,
            };
        }

        var cursor = 0;
        if (buffer[0] == 'y')
        {
            buffer[0] = 'Y';
            cursor = 1;
        }

        while (true)
        {
            // gopast v
            while (cursor < length && !IsVowel(buffer[cursor]))
            {
                cursor++;
            }

            if (cursor >= length)
            {
                return;
            }

            cursor++; // past the vowel
            if (cursor >= length)
            {
                return;
            }

            if (buffer[cursor] == 'i')
            {
                if (cursor + 1 < length && IsVowel(buffer[cursor + 1]))
                {
                    buffer[cursor] = 'I';
                }

                cursor++;
            }
            else if (buffer[cursor] == 'y')
            {
                buffer[cursor] = 'Y';
                cursor++;
            }
        }
    }

    // Advances past the next vowel, then past the next non-vowel; returns the region start or -1.
    private static int FindRegionStart(char[] buffer, int length, int from)
    {
        var cursor = from;
        while (cursor < length && !IsVowel(buffer[cursor]))
        {
            cursor++;
        }

        if (cursor >= length)
        {
            return -1;
        }

        cursor++;
        while (cursor < length && IsVowel(buffer[cursor]))
        {
            cursor++;
        }

        if (cursor >= length)
        {
            return -1;
        }

        return cursor + 1;
    }

    // R1/R2 per the Snowball definition; R1 is adjusted so that at least 3 letters precede it.
    private static void MarkRegions(char[] buffer, int length, out int p1, out int p2)
    {
        p1 = length;
        p2 = length;
        if (length < 3)
        {
            return;
        }

        var r1 = FindRegionStart(buffer, length, 0);
        if (r1 < 0)
        {
            return;
        }

        p1 = r1 < 3 ? 3 : r1;

        var r2 = FindRegionStart(buffer, length, r1);
        if (r2 >= 0)
        {
            p2 = r2;
        }
    }

    // Step 1: longest of heden / ene / en / se / s, with heden → heid in R1, en/ene per the
    // en-ending rule and s/se after a valid s-ending (a non-vowel other than j).
    private static int Step1(char[] buffer, int length, int p1)
    {
        if (EndsWith(buffer, length, "heden"))
        {
            var start = length - 5;
            if (start >= p1)
            {
                buffer[start + 2] = 'i';
                buffer[start + 3] = 'd';
                return length - 1;
            }

            return length;
        }

        if (EndsWith(buffer, length, "ene"))
        {
            return RemoveEnEnding(buffer, length, p1, 3);
        }

        if (EndsWith(buffer, length, "en"))
        {
            return RemoveEnEnding(buffer, length, p1, 2);
        }

        if (EndsWith(buffer, length, "se"))
        {
            return RemoveSEnding(buffer, length, p1, 2);
        }

        if (EndsWith(buffer, length, "s"))
        {
            return RemoveSEnding(buffer, length, p1, 1);
        }

        return length;
    }

    // Deletes an en/ene suffix in R1 when preceded by a valid en-ending (a non-vowel, and not
    // the string gem), then undoubles the ending.
    private static int RemoveEnEnding(char[] buffer, int length, int p1, int suffixLength)
    {
        var start = length - suffixLength;
        if (start < p1 || start < 1 || IsVowel(buffer[start - 1]))
        {
            return length;
        }

        if (start >= 3 && buffer[start - 3] == 'g' && buffer[start - 2] == 'e' && buffer[start - 1] == 'm')
        {
            return length;
        }

        return Undouble(buffer, start);
    }

    // Deletes an s/se suffix in R1 when preceded by a valid s-ending (a non-vowel other than j).
    private static int RemoveSEnding(char[] buffer, int length, int p1, int suffixLength)
    {
        var start = length - suffixLength;
        if (start < p1 || start < 1)
        {
            return length;
        }

        var preceding = buffer[start - 1];
        return IsVowel(preceding) || preceding == 'j' ? length : start;
    }

    // Undoubles the ending: if the word ends kk, dd or tt, removes the last letter.
    private static int Undouble(char[] buffer, int length)
    {
        if (length >= 2 && buffer[length - 1] == buffer[length - 2]
            && buffer[length - 1] is 'k' or 'd' or 't')
        {
            return length - 1;
        }

        return length;
    }

    // Step 2: deletes a final e in R1 preceded by a non-vowel, then undoubles the ending.
    // eFound records whether an e was actually removed (used by step 3b's bar suffix).
    private static int Step2(char[] buffer, int length, int p1, out bool eFound)
    {
        eFound = false;
        if (length >= 2 && buffer[length - 1] == 'e' && length - 1 >= p1 && !IsVowel(buffer[length - 2]))
        {
            eFound = true;
            return Undouble(buffer, length - 1);
        }

        return length;
    }

    // Step 3a: deletes heid in R2 when not preceded by c, then treats a following en suffix
    // exactly as in step 1.
    private static int Step3A(char[] buffer, int length, int p1, int p2)
    {
        if (!EndsWith(buffer, length, "heid"))
        {
            return length;
        }

        var start = length - 4;
        if (start < p2 || (start >= 1 && buffer[start - 1] == 'c'))
        {
            return length;
        }

        length = start;
        if (EndsWith(buffer, length, "en"))
        {
            length = RemoveEnEnding(buffer, length, p1, 2);
        }

        return length;
    }

    // Step 3b: longest of end / ing / ig / lijk / baar / bar (d-suffixes), all conditioned on R2.
    private static int Step3B(char[] buffer, int length, int p1, int p2, bool eFound)
    {
        if (EndsWith(buffer, length, "baar"))
        {
            return length - 4 >= p2 ? length - 4 : length;
        }

        if (EndsWith(buffer, length, "lijk"))
        {
            if (length - 4 < p2)
            {
                return length;
            }

            // Delete, then repeat step 2 (its eFound result is no longer needed).
            return Step2(buffer, length - 4, p1, out _);
        }

        if (EndsWith(buffer, length, "end") || EndsWith(buffer, length, "ing"))
        {
            var start = length - 3;
            if (start < p2)
            {
                return length;
            }

            length = start;
            if (EndsWith(buffer, length, "ig") && length - 2 >= p2
                && (length < 3 || buffer[length - 3] != 'e'))
            {
                return length - 2;
            }

            return Undouble(buffer, length);
        }

        if (EndsWith(buffer, length, "bar"))
        {
            return eFound && length - 3 >= p2 ? length - 3 : length;
        }

        if (EndsWith(buffer, length, "ig"))
        {
            var start = length - 2;
            if (start >= p2 && (start < 1 || buffer[start - 1] != 'e'))
            {
                return start;
            }

            return length;
        }

        return length;
    }

    // Step 4 (undouble vowel): if the word ends CVD with C a non-vowel, V a double a/e/o/u and
    // D a non-vowel other than I, remove one letter of V.
    private static int Step4(char[] buffer, int length)
    {
        if (length < 4)
        {
            return length;
        }

        var last = buffer[length - 1];
        if (IsVowel(last) || last == 'I')
        {
            return length;
        }

        var vowel = buffer[length - 2];
        if (vowel != buffer[length - 3] || vowel is not ('a' or 'e' or 'o' or 'u') || IsVowel(buffer[length - 4]))
        {
            return length;
        }

        buffer[length - 2] = last;
        return length - 1;
    }

    // Postlude: turns the Y/I consonant markers back into lower case.
    private static void Postlude(char[] buffer, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (buffer[i] == 'Y')
            {
                buffer[i] = 'y';
            }
            else if (buffer[i] == 'I')
            {
                buffer[i] = 'i';
            }
        }
    }
}
