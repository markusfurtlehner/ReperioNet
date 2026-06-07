using System.Text;
using ReperioNet.Abstractions;

namespace ReperioNet.Languages.De;

/// <summary>
/// Faithful managed port of the official Snowball "german" stemming algorithm
/// (https://snowballstem.org/algorithms/german/stemmer.html, classic revision as shipped through
/// Snowball 2.2): ß → ss preprocessing; u/y between vowels marked as consonants (U/Y trick);
/// R1/R2 with the adjustment that R1 begins no earlier than position 3; suffix steps 1–3 with the
/// published suffix lists applied longest-match-first; and the final substitution
/// ä → a, ö → o, ü → u.
/// </summary>
/// <remarks>
/// Expects tokens that are already lowercased with diacritics preserved (e.g. <c>"müller"</c>) and
/// returns lowercase stems. Thread-safe: the type is stateless and all working state lives in
/// locals, so <see cref="Stem"/> may be called concurrently.
/// </remarks>
public sealed class SnowballGermanStemmer : IStemmer
{
    /// <inheritdoc />
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return token ?? string.Empty;
        }

        var word = Prelude(token);
        MarkRegions(word, out var p1, out var p2);
        Step1(word, p1);
        Step2(word, p1);
        Step3(word, p1, p2);
        return Postlude(word);
    }

    /// <summary>Snowball <c>prelude</c>: replace ß with ss, then mark u/y between vowels as U/Y.</summary>
    private static StringBuilder Prelude(string token)
    {
        var word = new StringBuilder(token.Length + 4);
        foreach (var c in token)
        {
            if (c == 'ß')
            {
                word.Append("ss");
            }
            else
            {
                word.Append(c);
            }
        }

        // Left-to-right scan; a mark made here is visible to later checks, matching the
        // "repeat goto" semantics of the Snowball source.
        for (var i = 1; i + 1 < word.Length; i++)
        {
            var c = word[i];
            if ((c == 'u' || c == 'y') && IsVowel(word[i - 1]) && IsVowel(word[i + 1]))
            {
                word[i] = c == 'u' ? 'U' : 'Y';
            }
        }

        return word;
    }

    /// <summary>
    /// Snowball <c>mark_regions</c>: R1 is the region after the first non-vowel following a vowel,
    /// R2 the same computed within R1; R1 is then adjusted so the region before it has at least
    /// 3 letters. R2 is intentionally derived from the unadjusted R1 cursor, as in the original.
    /// </summary>
    private static void MarkRegions(StringBuilder word, out int p1, out int p2)
    {
        var limit = word.Length;
        p1 = limit;
        p2 = limit;
        if (limit < 3)
        {
            // test(hop 3) fails: both regions stay at the limit.
            return;
        }

        var cursor = 0;
        if (!GoPastVowel(word, ref cursor) || !GoPastNonVowel(word, ref cursor))
        {
            return;
        }

        var rawP1 = cursor;
        if (GoPastVowel(word, ref cursor) && GoPastNonVowel(word, ref cursor))
        {
            p2 = cursor;
        }

        p1 = Math.Max(rawP1, 3);
    }

    /// <summary>Advances <paramref name="cursor"/> just past the next vowel; false if none remains.</summary>
    private static bool GoPastVowel(StringBuilder word, ref int cursor)
    {
        while (cursor < word.Length)
        {
            var isVowel = IsVowel(word[cursor]);
            cursor++;
            if (isVowel)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Advances <paramref name="cursor"/> just past the next non-vowel; false if none remains.</summary>
    private static bool GoPastNonVowel(StringBuilder word, ref int cursor)
    {
        while (cursor < word.Length)
        {
            var isVowel = IsVowel(word[cursor]);
            cursor++;
            if (!isVowel)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Step 1: longest match among <c>em ern er e en es s</c>; delete if in R1. After deleting
    /// <c>e</c>/<c>en</c>/<c>es</c>, a trailing "niss" loses its final s. The suffix <c>s</c>
    /// requires a valid s-ending before it.
    /// </summary>
    private static void Step1(StringBuilder word, int p1)
    {
        var length = word.Length;
        if (EndsWith(word, "ern"))
        {
            if (length - 3 >= p1)
            {
                word.Length = length - 3;
            }

            return;
        }

        if (EndsWith(word, "em"))
        {
            if (length - 2 >= p1)
            {
                word.Length = length - 2;
            }

            return;
        }

        if (EndsWith(word, "er"))
        {
            if (length - 2 >= p1)
            {
                word.Length = length - 2;
            }

            return;
        }

        if (EndsWith(word, "en") || EndsWith(word, "es"))
        {
            if (length - 2 >= p1)
            {
                word.Length = length - 2;
                TryDropNissS(word);
            }

            return;
        }

        if (EndsWith(word, "e"))
        {
            if (length - 1 >= p1)
            {
                word.Length = length - 1;
                TryDropNissS(word);
            }

            return;
        }

        if (EndsWith(word, "s"))
        {
            if (length - 1 >= p1 && length >= 2 && IsValidSEnding(word[length - 2]))
            {
                word.Length = length - 1;
            }
        }
    }

    /// <summary>Snowball <c>try (['s'] 'nis' delete)</c>: drop the final s of a trailing "niss".</summary>
    private static void TryDropNissS(StringBuilder word)
    {
        if (EndsWith(word, "niss"))
        {
            word.Length--;
        }
    }

    /// <summary>
    /// Step 2: longest match among <c>en er est st</c>; delete if in R1. The suffix <c>st</c>
    /// additionally requires a valid st-ending before it, itself preceded by at least 3 letters.
    /// </summary>
    private static void Step2(StringBuilder word, int p1)
    {
        var length = word.Length;
        if (EndsWith(word, "est"))
        {
            if (length - 3 >= p1)
            {
                word.Length = length - 3;
            }

            return;
        }

        if (EndsWith(word, "en") || EndsWith(word, "er"))
        {
            if (length - 2 >= p1)
            {
                word.Length = length - 2;
            }

            return;
        }

        if (EndsWith(word, "st"))
        {
            if (length - 2 >= p1 && length >= 6 && IsValidStEnding(word[length - 3]))
            {
                word.Length = length - 2;
            }
        }
    }

    /// <summary>
    /// Step 3 (d-suffixes): longest match among <c>end ung ig ik isch lich heit keit</c> with the
    /// published R1/R2 and "not preceded by e" conditions and follow-up deletions.
    /// </summary>
    private static void Step3(StringBuilder word, int p1, int p2)
    {
        var length = word.Length;

        // 'end' 'ung': delete if in R2; then a preceding 'ig' (in R2, not preceded by 'e') goes too.
        if (EndsWith(word, "end") || EndsWith(word, "ung"))
        {
            if (length - 3 >= p2)
            {
                word.Length = length - 3;
                var inner = word.Length;
                if (EndsWith(word, "ig") && inner - 2 >= p2 && !PrecededBy(word, inner - 2, 'e'))
                {
                    word.Length = inner - 2;
                }
            }

            return;
        }

        // 'isch' 'lich' 'heit' 'keit' are all length 4 and mutually exclusive by their endings.
        if (EndsWith(word, "isch"))
        {
            if (length - 4 >= p2 && !PrecededBy(word, length - 4, 'e'))
            {
                word.Length = length - 4;
            }

            return;
        }

        if (EndsWith(word, "lich") || EndsWith(word, "heit"))
        {
            if (length - 4 >= p2)
            {
                word.Length = length - 4;
                var inner = word.Length;
                if ((EndsWith(word, "er") || EndsWith(word, "en")) && inner - 2 >= p1)
                {
                    word.Length = inner - 2;
                }
            }

            return;
        }

        if (EndsWith(word, "keit"))
        {
            if (length - 4 >= p2)
            {
                word.Length = length - 4;
                var inner = word.Length;
                if (EndsWith(word, "lich") && inner - 4 >= p2)
                {
                    word.Length = inner - 4;
                }
                else if (EndsWith(word, "ig") && inner - 2 >= p2)
                {
                    word.Length = inner - 2;
                }
            }

            return;
        }

        // 'ig' 'ik': delete if in R2 and not preceded by 'e'.
        if (EndsWith(word, "ig") || EndsWith(word, "ik"))
        {
            if (length - 2 >= p2 && !PrecededBy(word, length - 2, 'e'))
            {
                word.Length = length - 2;
            }
        }
    }

    /// <summary>Snowball <c>postlude</c>: U/Y back to lower case and umlauts removed from a, o, u.</summary>
    private static string Postlude(StringBuilder word)
    {
        var result = new StringBuilder(word.Length);
        for (var i = 0; i < word.Length; i++)
        {
            var c = word[i];
            result.Append(c switch
            {
                'U' => 'u',
                'Y' => 'y',
                'ä' => 'a',
                'ö' => 'o',
                'ü' => 'u',
                _ => c,
            });
        }

        return result.ToString();
    }

    /// <summary>The Snowball vowel set; the marked consonants U and Y are deliberately excluded.</summary>
    private static bool IsVowel(char c)
        => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'ä' or 'ö' or 'ü';

    /// <summary>Valid s-ending: one of b, d, f, g, h, k, l, m, n, r, t.</summary>
    private static bool IsValidSEnding(char c)
        => c is 'b' or 'd' or 'f' or 'g' or 'h' or 'k' or 'l' or 'm' or 'n' or 'r' or 't';

    /// <summary>Valid st-ending: the valid s-endings excluding r.</summary>
    private static bool IsValidStEnding(char c)
        => c is 'b' or 'd' or 'f' or 'g' or 'h' or 'k' or 'l' or 'm' or 'n' or 't';

    /// <summary>True if <paramref name="word"/> currently ends with <paramref name="suffix"/>.</summary>
    private static bool EndsWith(StringBuilder word, string suffix)
    {
        if (word.Length < suffix.Length)
        {
            return false;
        }

        var offset = word.Length - suffix.Length;
        for (var i = 0; i < suffix.Length; i++)
        {
            if (word[offset + i] != suffix[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True if the character just before <paramref name="position"/> equals <paramref name="c"/>.</summary>
    private static bool PrecededBy(StringBuilder word, int position, char c)
        => position > 0 && word[position - 1] == c;
}
