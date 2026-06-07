// Faithful port of the official Snowball "english" stemmer (Porter2):
// https://snowballstem.org/algorithms/english/stemmer.html
// Ported from the reference Snowball source (english.sbl), including the exceptional
// word forms, the special R1 prefixes, y/Y vowel marking, steps 0 and 1a-5, and the
// short-word / short-syllable rules.
using ReperioNet.Abstractions;

namespace ReperioNet.Languages.En;

/// <summary>
/// The Snowball "english" stemmer (Porter2) as published at
/// <see href="https://snowballstem.org/algorithms/english/stemmer.html"/>.
/// Stateless and safe for concurrent use; expects lowercased tokens and returns lowercase stems.
/// </summary>
public sealed class SnowballEnglishStemmer : IStemmer
{
    /// <summary>Exceptional word forms applied before the algorithm (exception1 in the reference source).</summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        // Special changes.
        ["skis"] = "ski",
        ["skies"] = "sky",

        // Special -ly cases.
        ["idly"] = "idl",
        ["gently"] = "gentl",
        ["ugly"] = "ugli",
        ["early"] = "earli",
        ["only"] = "onli",
        ["singly"] = "singl",

        // Invariant forms.
        ["sky"] = "sky",
        ["news"] = "news",
        ["howe"] = "howe",
        ["atlas"] = "atlas",
        ["cosmos"] = "cosmos",
        ["bias"] = "bias",
        ["andes"] = "andes",
    };

    /// <summary>Words beginning with these prefixes have R1 set to the remainder of the word.</summary>
    private static readonly string[] R1Prefixes =
    {
        "univers", "commun", "arsen", "emerg", "gener", "inter", "later", "organ", "past",
    };

    /// <summary>Step 1b suffixes in longest-match-first order.</summary>
    private static readonly string[] Step1BSuffixes = { "eedly", "ingly", "edly", "eed", "ing", "ed" };

    /// <summary>Step 2 suffix replacements in longest-match-first order ("ogi" and "li" carry extra conditions and are handled separately).</summary>
    private static readonly (string Suffix, string Replacement)[] Step2Suffixes =
    {
        ("ational", "ate"),
        ("fulness", "ful"),
        ("iveness", "ive"),
        ("ization", "ize"),
        ("ousness", "ous"),
        ("biliti", "ble"),
        ("lessli", "less"),
        ("tional", "tion"),
        ("alism", "al"),
        ("aliti", "al"),
        ("ation", "ate"),
        ("entli", "ent"),
        ("fulli", "ful"),
        ("iviti", "ive"),
        ("ogist", "og"),
        ("ousli", "ous"),
        ("alli", "al"),
        ("anci", "ance"),
        ("abli", "able"),
        ("ator", "ate"),
        ("enci", "ence"),
        ("izer", "ize"),
        ("bli", "ble"),
    };

    /// <summary>Step 3 suffix replacements in longest-match-first order; "ative" additionally requires R2.</summary>
    private static readonly (string Suffix, string Replacement, bool RequiresR2)[] Step3Suffixes =
    {
        ("ational", "ate", false),
        ("tional", "tion", false),
        ("alize", "al", false),
        ("ative", "", true),
        ("icate", "ic", false),
        ("iciti", "ic", false),
        ("ical", "ic", false),
        ("ness", "", false),
        ("ful", "", false),
    };

    /// <summary>Step 4 suffixes in longest-match-first order ("ion" carries an extra condition and is handled separately).</summary>
    private static readonly string[] Step4Suffixes =
    {
        "ement",
        "able", "ance", "ence", "ible", "ment",
        "ant", "ate", "ent", "ism", "iti", "ive", "ize", "ous",
        "al", "er", "ic",
    };

    /// <summary>Returns the Porter2 stem of <paramref name="token"/>.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    /// <returns>The lowercase stem.</returns>
    public string Stem(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (Exceptions.TryGetValue(token, out var exceptional))
        {
            return exceptional;
        }

        // Words of two letters or less are left as they are.
        if (token.Length < 3)
        {
            return token;
        }

        var word = token;

        // Prelude: remove an initial apostrophe, then mark y as a consonant (Y) where appropriate.
        if (word[0] == '\'')
        {
            word = word[1..];
        }

        word = MarkConsonantYs(word, out var yFound);

        ComputeRegions(word, out var p1, out var p2);

        word = Step0(word);
        word = Step1A(word);
        word = Step1B(word, p1);
        word = Step1C(word);
        word = Step2(word, p1);
        word = Step3(word, p1, p2);
        word = Step4(word, p2);
        word = Step5(word, p1, p2);

        // Postlude: turn any remaining Y letters back into lower case.
        return yFound ? word.Replace('Y', 'y') : word;
    }

    /// <summary>True for the Porter2 vowels a, e, i, o, u, y (the marked consonant Y is not a vowel).</summary>
    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    /// <summary>Sets an initial y, or a y after a vowel, to Y (scanning left to right over the evolving word).</summary>
    private static string MarkConsonantYs(string word, out bool yFound)
    {
        yFound = false;
        if (!word.Contains('y'))
        {
            return word;
        }

        var chars = word.ToCharArray();
        if (chars[0] == 'y')
        {
            chars[0] = 'Y';
            yFound = true;
        }

        for (var i = 1; i < chars.Length; i++)
        {
            if (chars[i] == 'y' && IsVowel(chars[i - 1]))
            {
                chars[i] = 'Y';
                yFound = true;
            }
        }

        return yFound ? new string(chars) : word;
    }

    /// <summary>
    /// Establishes R1 and R2. R1 is the region after the first non-vowel following a vowel
    /// (or after a special prefix), R2 the same computed within R1; both default to the word end.
    /// </summary>
    private static void ComputeRegions(string word, out int p1, out int p2)
    {
        p1 = word.Length;
        p2 = word.Length;

        var start = -1;
        foreach (var prefix in R1Prefixes)
        {
            if (word.StartsWith(prefix, StringComparison.Ordinal))
            {
                start = prefix.Length;
                break;
            }
        }

        if (start < 0)
        {
            start = FindRegionStart(word, 0);
        }

        if (start < 0)
        {
            return;
        }

        p1 = start;
        var second = FindRegionStart(word, p1);
        if (second >= 0)
        {
            p2 = second;
        }
    }

    /// <summary>Returns the index after the first non-vowel that follows a vowel at or after <paramref name="from"/>, or -1.</summary>
    private static int FindRegionStart(string word, int from)
    {
        var i = from;
        while (i < word.Length && !IsVowel(word[i]))
        {
            i++;
        }

        if (i == word.Length)
        {
            return -1;
        }

        i++;
        while (i < word.Length && IsVowel(word[i]))
        {
            i++;
        }

        return i == word.Length ? -1 : i + 1;
    }

    /// <summary>Step 0: removes the longest of the suffixes <c>'s'</c>, <c>'s</c>, <c>'</c>.</summary>
    private static string Step0(string word)
    {
        if (word.EndsWith("'s'", StringComparison.Ordinal))
        {
            return word[..^3];
        }

        if (word.EndsWith("'s", StringComparison.Ordinal))
        {
            return word[..^2];
        }

        return word.EndsWith('\'') ? word[..^1] : word;
    }

    /// <summary>Step 1a: plural endings (sses, ied/ies, s; us/ss are left alone).</summary>
    private static string Step1A(string word)
    {
        if (word.EndsWith("sses", StringComparison.Ordinal))
        {
            return word[..^2];
        }

        if (word.EndsWith("ied", StringComparison.Ordinal) || word.EndsWith("ies", StringComparison.Ordinal))
        {
            var stemPart = word[..^3];
            return stemPart.Length > 1 ? stemPart + "i" : stemPart + "ie";
        }

        if (word.EndsWith("us", StringComparison.Ordinal) || word.EndsWith("ss", StringComparison.Ordinal))
        {
            return word;
        }

        if (word.EndsWith('s'))
        {
            // Delete only if a vowel exists that is not immediately before the s.
            for (var i = 0; i < word.Length - 2; i++)
            {
                if (IsVowel(word[i]))
                {
                    return word[..^1];
                }
            }
        }

        return word;
    }

    /// <summary>Step 1b: eed/eedly and ed/edly/ing/ingly endings, with the -ing exceptional cases.</summary>
    private static string Step1B(string word, int p1)
    {
        string? suffix = null;
        foreach (var candidate in Step1BSuffixes)
        {
            if (word.EndsWith(candidate, StringComparison.Ordinal))
            {
                suffix = candidate;
                break;
            }
        }

        if (suffix is null)
        {
            return word;
        }

        if (suffix is "eed" or "eedly")
        {
            var start = word.Length - suffix.Length;
            if (start >= p1)
            {
                var stemPart = word[..start];

                // proceed, exceed, succeed (and inflections) keep their -eed.
                if (stemPart is not ("proc" or "exc" or "succ"))
                {
                    return stemPart + "ee";
                }
            }

            return word;
        }

        if (suffix == "ing")
        {
            var stemPart = word[..^3];

            // Exactly one non-vowel followed by y: dying -> die, lying -> lie, tying -> tie.
            if (stemPart.Length == 2 && !IsVowel(stemPart[0]) && stemPart[1] == 'y')
            {
                return stemPart[..1] + "ie";
            }

            // Leave inning, outing, canning, herring, earring, evening alone.
            if (stemPart is "inn" or "out" or "cann" or "herr" or "earr" or "even")
            {
                return word;
            }
        }

        // Common handling for ed, edly, ing, ingly: delete only if the preceding part contains a vowel.
        var preceding = word[..^suffix.Length];
        if (!ContainsVowel(preceding))
        {
            return word;
        }

        word = preceding;

        if (word.EndsWith("at", StringComparison.Ordinal)
            || word.EndsWith("bl", StringComparison.Ordinal)
            || word.EndsWith("iz", StringComparison.Ordinal))
        {
            return word + "e";
        }

        if (EndsWithDouble(word))
        {
            // hopp -> hop, but add, egg and off keep their double.
            return word.Length == 3 && word[0] is 'a' or 'e' or 'o' ? word : word[..^1];
        }

        return word.Length == p1 && IsShortSyllableAt(word, word.Length) ? word + "e" : word;
    }

    /// <summary>Step 1c: replaces a final y/Y by i when preceded by a non-vowel that is not the first letter.</summary>
    private static string Step1C(string word)
    {
        if (word.Length > 2 && (word[^1] == 'y' || word[^1] == 'Y') && !IsVowel(word[^2]))
        {
            return word[..^1] + "i";
        }

        return word;
    }

    /// <summary>Step 2: standard suffix replacements, applied to the longest matching suffix when it lies in R1.</summary>
    private static string Step2(string word, int p1)
    {
        foreach (var (suffix, replacement) in Step2Suffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal))
            {
                var start = word.Length - suffix.Length;
                return start >= p1 ? word[..start] + replacement : word;
            }
        }

        if (word.EndsWith("ogi", StringComparison.Ordinal))
        {
            var start = word.Length - 3;
            if (start >= p1 && start > 0 && word[start - 1] == 'l')
            {
                return word[..start] + "og";
            }

            return word;
        }

        if (word.EndsWith("li", StringComparison.Ordinal))
        {
            var start = word.Length - 2;
            if (start >= p1 && start > 0 && IsValidLiEnding(word[start - 1]))
            {
                return word[..start];
            }
        }

        return word;
    }

    /// <summary>Step 3: further suffix replacements, applied to the longest matching suffix when it lies in R1.</summary>
    private static string Step3(string word, int p1, int p2)
    {
        foreach (var (suffix, replacement, requiresR2) in Step3Suffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal))
            {
                var start = word.Length - suffix.Length;
                if (start >= p1 && (!requiresR2 || start >= p2))
                {
                    return word[..start] + replacement;
                }

                return word;
            }
        }

        return word;
    }

    /// <summary>Step 4: residual suffix deletion, applied to the longest matching suffix when it lies in R2.</summary>
    private static string Step4(string word, int p2)
    {
        foreach (var suffix in Step4Suffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal))
            {
                var start = word.Length - suffix.Length;
                return start >= p2 ? word[..start] : word;
            }
        }

        if (word.EndsWith("ion", StringComparison.Ordinal))
        {
            var start = word.Length - 3;
            if (start >= p2 && start > 0 && word[start - 1] is 's' or 't')
            {
                return word[..start];
            }
        }

        return word;
    }

    /// <summary>Step 5: removes a final e (R2, or R1 when not preceded by a short syllable) or one l of a final double l in R2.</summary>
    private static string Step5(string word, int p1, int p2)
    {
        if (word.Length == 0)
        {
            return word;
        }

        if (word[^1] == 'e')
        {
            var start = word.Length - 1;
            if (start >= p2 || (start >= p1 && !IsShortSyllableAt(word, start)))
            {
                return word[..^1];
            }

            return word;
        }

        if (word[^1] == 'l')
        {
            var start = word.Length - 1;
            if (start >= p2 && start > 0 && word[start - 1] == 'l')
            {
                return word[..^1];
            }
        }

        return word;
    }

    /// <summary>True if any character of <paramref name="text"/> is a vowel.</summary>
    private static bool ContainsVowel(string text)
    {
        foreach (var c in text)
        {
            if (IsVowel(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if the word ends in one of the doubles bb, dd, ff, gg, mm, nn, pp, rr, tt.</summary>
    private static bool EndsWithDouble(string word)
    {
        if (word.Length < 2)
        {
            return false;
        }

        var c = word[^1];
        return c == word[^2] && c is 'b' or 'd' or 'f' or 'g' or 'm' or 'n' or 'p' or 'r' or 't';
    }

    /// <summary>
    /// True if a short syllable ends at <paramref name="position"/>: a vowel followed by a non-vowel
    /// other than w, x or Y and preceded by a non-vowel; a vowel at the beginning of the word followed
    /// by a non-vowel; or the letters "past".
    /// </summary>
    private static bool IsShortSyllableAt(string word, int position)
    {
        if (position >= 3)
        {
            var c1 = word[position - 1];
            if (!IsVowel(c1) && c1 is not ('w' or 'x' or 'Y') && IsVowel(word[position - 2]) && !IsVowel(word[position - 3]))
            {
                return true;
            }
        }

        if (position == 2 && IsVowel(word[0]) && !IsVowel(word[1]))
        {
            return true;
        }

        return position >= 4 && string.CompareOrdinal(word, position - 4, "past", 0, 4) == 0;
    }

    /// <summary>True for the valid li-endings c, d, e, g, h, k, m, n, r, t.</summary>
    private static bool IsValidLiEnding(char c) => c is 'c' or 'd' or 'e' or 'g' or 'h' or 'k' or 'm' or 'n' or 'r' or 't';
}
