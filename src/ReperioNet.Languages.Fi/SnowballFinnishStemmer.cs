using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Fi;

/// <summary>
/// Snowball stemmer for Finnish; a faithful port of the official algorithm published at
/// <see href="https://snowballstem.org/algorithms/finnish/stemmer.html"/>.
/// </summary>
/// <remarks>
/// The implementation keeps all working state in locals, so a single instance is safe for
/// concurrent use.
/// </remarks>
public sealed class SnowballFinnishStemmer : IStemmer
{
    private const string Vowels = "aeiouyäö";
    private const string RestrictedVowels = "aeiouäö";
    private const string Consonants = "bcdfghjklmnpqrstvwxz";
    private const string AeiVowels = "aäei";
    private const string ParticleEnd = "aeiouyäönt";

    // Longest first; find_among_b picks the longest entry that fits inside the region.
    private static readonly string[] ParticleSuffixes =
    [
        "kaan", "kään", "kin", "han", "hän", "sti", "ko", "kö", "pa", "pä",
    ];

    private static readonly string[] OtherEndingSuffixes =
    [
        "impi", "impa", "impä", "immi", "imma", "immä",
        "mpi", "mpa", "mpä", "mmi", "mma", "mmä", "eja", "ejä",
    ];

    /// <inheritdoc />
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return token;
        }

        var s = token;
        MarkRegions(s, out var p1, out var p2);

        var endingRemoved = false;
        s = RemoveParticle(s, p1, p2);
        s = RemovePossessive(s, p1);
        s = RemoveCaseEnding(s, p1, ref endingRemoved);
        s = RemoveOtherEndings(s, p2);
        s = endingRemoved ? RemoveIPlural(s, p1) : RemoveTPlural(s, p1, p2);
        s = Tidy(s, p1);
        return s;
    }

    private static bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;

    private static void MarkRegions(string s, out int p1, out int p2)
    {
        p1 = s.Length;
        p2 = s.Length;

        var i = GoPastVowelThenNonVowel(s, 0);
        if (i < 0)
        {
            return;
        }

        p1 = i;
        i = GoPastVowelThenNonVowel(s, i);
        if (i < 0)
        {
            return;
        }

        p2 = i;
    }

    /// <summary>Performs <c>gopast v gopast non-v</c> starting at <paramref name="start"/>; -1 on failure.</summary>
    private static int GoPastVowelThenNonVowel(string s, int start)
    {
        var i = start;
        while (i < s.Length && !IsVowel(s[i]))
        {
            i++;
        }

        if (i == s.Length)
        {
            return -1;
        }

        i++;
        while (i < s.Length && IsVowel(s[i]))
        {
            i++;
        }

        if (i == s.Length)
        {
            return -1;
        }

        return i + 1;
    }

    /// <summary>Tests that <paramref name="suffix"/> ends <paramref name="s"/> entirely within the region starting at <paramref name="limit"/>.</summary>
    private static bool EndsWithIn(string s, string suffix, int limit)
        => s.Length - suffix.Length >= limit && s.EndsWith(suffix, StringComparison.Ordinal);

    private static string RemoveParticle(string s, int p1, int p2)
    {
        foreach (var suffix in ParticleSuffixes)
        {
            if (!EndsWithIn(s, suffix, p1))
            {
                continue;
            }

            if (suffix == "sti")
            {
                // Adverb suffix: delete only in R2.
                return s.Length - suffix.Length >= p2 ? s[..^3] : s;
            }

            // Particles: delete when preceded by n, t or a vowel.
            var prev = s.Length - suffix.Length - 1;
            return prev >= 0 && ParticleEnd.IndexOf(s[prev]) >= 0 ? s[..(s.Length - suffix.Length)] : s;
        }

        return s;
    }

    private static string RemovePossessive(string s, int p1)
    {
        if (EndsWithIn(s, "nsa", p1) || EndsWithIn(s, "nsä", p1)
            || EndsWithIn(s, "mme", p1) || EndsWithIn(s, "nne", p1))
        {
            return s[..^3];
        }

        if (EndsWithIn(s, "si", p1))
        {
            // Keep "ksi" intact: it is the comitative case ending.
            var prev = s.Length - 3;
            return prev >= 0 && s[prev] == 'k' ? s : s[..^2];
        }

        if (EndsWithIn(s, "ni", p1))
        {
            s = s[..^2];

            // "kseni" = "ksi" + "ni": restore the case ending's final vowel.
            return s.EndsWith("kse", StringComparison.Ordinal) ? s[..^1] + "i" : s;
        }

        if (EndsWithIn(s, "an", p1))
        {
            var stem = s[..^2];
            return EndsWithAny(stem, "ta", "ssa", "sta", "lla", "lta", "na") ? stem : s;
        }

        if (EndsWithIn(s, "än", p1))
        {
            var stem = s[..^2];
            return EndsWithAny(stem, "tä", "ssä", "stä", "llä", "ltä", "nä") ? stem : s;
        }

        if (EndsWithIn(s, "en", p1))
        {
            var stem = s[..^2];
            return EndsWithAny(stem, "lle", "ine") ? stem : s;
        }

        return s;
    }

    private static bool EndsWithAny(string s, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests for a long vowel (aa, ee, ii, oo, uu, ää, öö) ending at index <paramref name="end"/>
    /// (exclusive), reading no character left of <paramref name="limit"/>.
    /// </summary>
    private static bool LongVowelEndsAt(string s, int end, int limit = 0)
        => end - 2 >= limit && s[end - 1] == s[end - 2] && RestrictedVowels.IndexOf(s[end - 1]) >= 0;

    /// <summary>
    /// Tests for a restricted vowel + i pair (or an apostrophe) ending at index
    /// <paramref name="end"/> (exclusive), reading no character left of <paramref name="limit"/>.
    /// </summary>
    private static bool ViEndsAt(string s, int end, int limit)
    {
        if (end - 1 >= limit && s[end - 1] == '\'')
        {
            return true;
        }

        return end - 2 >= limit && s[end - 1] == 'i' && RestrictedVowels.IndexOf(s[end - 2]) >= 0;
    }

    private static string RemoveCaseEnding(string s, int p1, ref bool endingRemoved)
    {
        // The illative/genitive-plural conditions below are attached to the among entries as
        // routine calls in the Snowball source: they are evaluated inside the R1 limit (they may
        // not look left of p1) and, when one fails, find_among_b backtracks and continues with
        // the shorter entries (typically falling through to "n").

        // siin/tten: preceded by Vi; seen: preceded by a long vowel.
        if ((EndsWithIn(s, "siin", p1) || EndsWithIn(s, "tten", p1)) && ViEndsAt(s, s.Length - 4, p1))
        {
            endingRemoved = true;
            return s[..^4];
        }

        if (EndsWithIn(s, "seen", p1) && LongVowelEndsAt(s, s.Length - 4, p1))
        {
            endingRemoved = true;
            return s[..^4];
        }

        // hVn (illative): preceded by the matching vowel (or an apostrophe, for elided stems).
        foreach (var (suffix, vowel) in HVnSuffixes)
        {
            if (!EndsWithIn(s, suffix, p1))
            {
                continue;
            }

            var prev = s.Length - 4;
            if (prev >= p1 && (s[prev] == vowel || s[prev] == '\'' || (suffix == "hön" && s[prev] == 'ø')))
            {
                endingRemoved = true;
                return s[..^3];
            }

            // Condition failed: backtrack to the shorter entries.
            break;
        }

        if (EndsWithIn(s, "den", p1) && ViEndsAt(s, s.Length - 3, p1))
        {
            endingRemoved = true;
            return s[..^3];
        }

        if (EndsWithIn(s, "tta", p1) || EndsWithIn(s, "ttä", p1))
        {
            // Partitive: preceded by e.
            var prev = s.Length - 4;
            if (prev >= 0 && s[prev] == 'e')
            {
                endingRemoved = true;
                return s[..^3];
            }

            return s;
        }

        if (EndsWithAnyIn(s, p1, "ssa", "ssä", "sta", "stä", "lla", "llä", "lta", "ltä", "lle", "ksi", "ine"))
        {
            endingRemoved = true;
            return s[..^3];
        }

        if (EndsWithAnyIn(s, p1, "ta", "tä", "na", "nä"))
        {
            endingRemoved = true;
            return s[..^2];
        }

        if (EndsWithIn(s, "a", p1) || EndsWithIn(s, "ä", p1))
        {
            // Partitive: preceded by a consonant + vowel pair.
            var prev = s.Length - 2;
            if (prev >= 1 && IsVowel(s[prev]) && Consonants.IndexOf(s[prev - 1]) >= 0)
            {
                endingRemoved = true;
                return s[..^1];
            }

            return s;
        }

        if (EndsWithIn(s, "n", p1))
        {
            // Genitive or illative; after a long vowel or "ie", the last vowel goes too.
            s = s[..^1];
            if (LongVowelEndsAt(s, s.Length) || s.EndsWith("ie", StringComparison.Ordinal))
            {
                s = s[..^1];
            }

            endingRemoved = true;
            return s;
        }

        return s;
    }

    private static readonly (string Suffix, char Vowel)[] HVnSuffixes =
    [
        ("hän", 'ä'), ("hön", 'ö'), ("han", 'a'), ("hen", 'e'), ("hin", 'i'), ("hon", 'o'), ("hun", 'u'),
    ];

    private static bool EndsWithAnyIn(string s, int limit, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (EndsWithIn(s, suffix, limit))
            {
                return true;
            }
        }

        return false;
    }

    private static string RemoveOtherEndings(string s, int p2)
    {
        foreach (var suffix in OtherEndingSuffixes)
        {
            if (!EndsWithIn(s, suffix, p2))
            {
                continue;
            }

            if (suffix.Length == 3 && suffix[0] == 'm')
            {
                // Comparative forms: not removed when preceded by "po".
                var stem = s[..^3];
                return stem.EndsWith("po", StringComparison.Ordinal) ? s : stem;
            }

            return s[..(s.Length - suffix.Length)];
        }

        return s;
    }

    private static string RemoveIPlural(string s, int p1)
        => EndsWithIn(s, "i", p1) || EndsWithIn(s, "j", p1) ? s[..^1] : s;

    private static string RemoveTPlural(string s, int p1, int p2)
    {
        if (!EndsWithIn(s, "t", p1) || s.Length - 2 < p1 || !IsVowel(s[^2]))
        {
            return s;
        }

        s = s[..^1];
        if (EndsWithIn(s, "imma", p2))
        {
            return s[..^4];
        }

        if (EndsWithIn(s, "mma", p2))
        {
            var stem = s[..^3];
            return stem.EndsWith("po", StringComparison.Ordinal) ? s : stem;
        }

        return s;
    }

    private static string Tidy(string s, int p1)
    {
        // Undouble a final long vowel (within R1).
        if (s.Length - 2 >= p1 && LongVowelEndsAt(s, s.Length))
        {
            s = s[..^1];
        }

        // Remove a trailing a, ä, e or i after a consonant (within R1).
        if (s.Length - 2 >= p1 && AeiVowels.IndexOf(s[^1]) >= 0 && Consonants.IndexOf(s[^2]) >= 0)
        {
            s = s[..^1];
        }

        // Remove a trailing j after o or u (within R1).
        if (s.Length - 2 >= p1 && s[^1] == 'j' && (s[^2] == 'o' || s[^2] == 'u'))
        {
            s = s[..^1];
        }

        // Remove a trailing o after j (within R1).
        if (s.Length - 2 >= p1 && s[^1] == 'o' && s[^2] == 'j')
        {
            s = s[..^1];
        }

        // Undouble the rightmost non-vowel when it is a doubled consonant.
        var j = s.Length - 1;
        while (j >= 0 && IsVowel(s[j]))
        {
            j--;
        }

        if (j >= 1 && Consonants.IndexOf(s[j]) >= 0 && s[j - 1] == s[j])
        {
            s = s.Remove(j, 1);
        }

        // Remove a trailing apostrophe.
        if (s.Length > 0 && s[^1] == '\'')
        {
            s = s[..^1];
        }

        return s;
    }
}
