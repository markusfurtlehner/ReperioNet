using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Ru;

/// <summary>
/// Snowball stemmer for Russian; a faithful port of the official algorithm published at
/// <see href="https://snowballstem.org/algorithms/russian/stemmer.html"/>. Operates on
/// lowercase Cyrillic input; <c>ё</c> is normalized to <c>е</c> before stemming.
/// </summary>
/// <remarks>
/// The implementation keeps all working state in locals, so a single instance is safe for
/// concurrent use.
/// </remarks>
public sealed class SnowballRussianStemmer : IStemmer
{
    private const string Vowels = "аеиоуыэюя";

    // Suffix tables, longest first; a bool of true marks entries that require a preceding а or я.
    private static readonly (string Suffix, bool NeedsAYa)[] PerfectiveGerundSuffixes =
    [
        ("ившись", false), ("ывшись", false), ("вшись", true),
        ("ивши", false), ("ывши", false), ("вши", true),
        ("ив", false), ("ыв", false), ("в", true),
    ];

    private static readonly string[] AdjectiveSuffixes =
    [
        "ими", "ыми", "его", "ого", "ему", "ому",
        "ее", "ие", "ые", "ое", "ей", "ий", "ый", "ой", "ем", "им", "ым", "ом",
        "их", "ых", "ую", "юю", "ая", "яя", "ою", "ею",
    ];

    private static readonly (string Suffix, bool NeedsAYa)[] ParticipleSuffixes =
    [
        ("ивш", false), ("ывш", false), ("ующ", false),
        ("ем", true), ("нн", true), ("вш", true), ("ющ", true), ("щ", true),
    ];

    private static readonly (string Suffix, bool NeedsAYa)[] VerbSuffixes =
    [
        ("ейте", false), ("уйте", false),
        ("ете", true), ("йте", true), ("ешь", true), ("нно", true),
        ("ила", false), ("ыла", false), ("ена", false), ("ите", false), ("или", false), ("ыли", false),
        ("ило", false), ("ыло", false), ("ено", false), ("ует", false), ("уют", false), ("ены", false),
        ("ить", false), ("ыть", false), ("ишь", false),
        ("ла", true), ("на", true), ("ли", true), ("ем", true), ("ло", true), ("но", true),
        ("ет", true), ("ют", true), ("ны", true), ("ть", true),
        ("ей", false), ("уй", false), ("ил", false), ("ыл", false), ("им", false), ("ым", false),
        ("ен", false), ("ят", false), ("ит", false), ("ыт", false), ("ую", false),
        ("й", true), ("л", true), ("н", true),
        ("ю", false),
    ];

    private static readonly string[] NounSuffixes =
    [
        "иями",
        "ями", "ами", "ией", "иям", "ием", "иях",
        "ев", "ов", "ие", "ье", "еи", "ии", "ей", "ой", "ий", "ям", "ем", "ам", "ом",
        "ах", "ях", "ию", "ью", "ия", "ья",
        "а", "е", "и", "й", "о", "у", "ы", "ь", "ю", "я",
    ];

    /// <inheritdoc />
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return token;
        }

        var s = token.Replace('ё', 'е');
        MarkRegions(s, out var pV, out var p2);

        // All steps operate inside RV (the region after the first vowel).
        if (!RemovePerfectiveGerund(ref s, pV))
        {
            RemoveReflexive(ref s, pV);
            if (!RemoveAdjectival(ref s, pV) && !RemoveVerb(ref s, pV))
            {
                RemoveNoun(ref s, pV);
            }
        }

        if (EndsWithIn(s, "и", pV))
        {
            s = s[..^1];
        }

        RemoveDerivational(ref s, p2);
        TidyUp(ref s, pV);
        return s;
    }

    private static bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;

    private static void MarkRegions(string s, out int pV, out int p2)
    {
        pV = s.Length;
        p2 = s.Length;

        var i = 0;
        while (i < s.Length && !IsVowel(s[i]))
        {
            i++;
        }

        if (i == s.Length)
        {
            return;
        }

        pV = ++i;
        while (i < s.Length && IsVowel(s[i]))
        {
            i++;
        }

        if (i == s.Length)
        {
            return;
        }

        i++;
        while (i < s.Length && !IsVowel(s[i]))
        {
            i++;
        }

        if (i == s.Length)
        {
            return;
        }

        i++;
        while (i < s.Length && IsVowel(s[i]))
        {
            i++;
        }

        if (i == s.Length)
        {
            return;
        }

        p2 = i + 1;
    }

    /// <summary>Tests that <paramref name="suffix"/> ends <paramref name="s"/> entirely within the region starting at <paramref name="limit"/>.</summary>
    private static bool EndsWithIn(string s, string suffix, int limit)
        => s.Length - suffix.Length >= limit && s.EndsWith(suffix, StringComparison.Ordinal);

    /// <summary>Tests that the character before a suffix of <paramref name="suffixLength"/> is а or я, inside the region.</summary>
    private static bool PrecededByAYa(string s, int suffixLength, int limit)
    {
        var idx = s.Length - suffixLength - 1;
        return idx >= limit && (s[idx] == 'а' || s[idx] == 'я');
    }

    private static bool RemovePerfectiveGerund(ref string s, int pV)
    {
        foreach (var (suffix, needsAYa) in PerfectiveGerundSuffixes)
        {
            if (!EndsWithIn(s, suffix, pV))
            {
                continue;
            }

            if (needsAYa && !PrecededByAYa(s, suffix.Length, pV))
            {
                // Longest match committed; a failed condition fails the whole step.
                return false;
            }

            s = s[..(s.Length - suffix.Length)];
            return true;
        }

        return false;
    }

    private static void RemoveReflexive(ref string s, int pV)
    {
        if (EndsWithIn(s, "ся", pV) || EndsWithIn(s, "сь", pV))
        {
            s = s[..^2];
        }
    }

    private static bool RemoveAdjectival(ref string s, int pV)
    {
        var matched = false;
        foreach (var suffix in AdjectiveSuffixes)
        {
            if (EndsWithIn(s, suffix, pV))
            {
                s = s[..(s.Length - suffix.Length)];
                matched = true;
                break;
            }
        }

        if (!matched)
        {
            return false;
        }

        // Optionally remove a preceding participle suffix.
        foreach (var (suffix, needsAYa) in ParticipleSuffixes)
        {
            if (!EndsWithIn(s, suffix, pV))
            {
                continue;
            }

            if (!needsAYa || PrecededByAYa(s, suffix.Length, pV))
            {
                s = s[..(s.Length - suffix.Length)];
            }

            break;
        }

        return true;
    }

    private static bool RemoveVerb(ref string s, int pV)
    {
        foreach (var (suffix, needsAYa) in VerbSuffixes)
        {
            if (!EndsWithIn(s, suffix, pV))
            {
                continue;
            }

            if (needsAYa && !PrecededByAYa(s, suffix.Length, pV))
            {
                return false;
            }

            s = s[..(s.Length - suffix.Length)];
            return true;
        }

        return false;
    }

    private static void RemoveNoun(ref string s, int pV)
    {
        foreach (var suffix in NounSuffixes)
        {
            if (EndsWithIn(s, suffix, pV))
            {
                s = s[..(s.Length - suffix.Length)];
                return;
            }
        }
    }

    private static void RemoveDerivational(ref string s, int p2)
    {
        if (EndsWithIn(s, "ость", p2))
        {
            s = s[..^4];
        }
        else if (EndsWithIn(s, "ост", p2))
        {
            s = s[..^3];
        }
    }

    private static void TidyUp(ref string s, int pV)
    {
        if (EndsWithIn(s, "ейше", pV))
        {
            s = s[..^4];
            UndoubleN(ref s, pV);
            return;
        }

        if (EndsWithIn(s, "ейш", pV))
        {
            s = s[..^3];
            UndoubleN(ref s, pV);
            return;
        }

        if (EndsWithIn(s, "н", pV))
        {
            if (PrecededByN(s, pV))
            {
                s = s[..^1];
            }

            return;
        }

        if (EndsWithIn(s, "ь", pV))
        {
            s = s[..^1];
        }
    }

    private static bool PrecededByN(string s, int pV)
    {
        var idx = s.Length - 2;
        return idx >= pV && s[idx] == 'н';
    }

    private static void UndoubleN(ref string s, int pV)
    {
        if (EndsWithIn(s, "н", pV) && PrecededByN(s, pV))
        {
            s = s[..^1];
        }
    }
}
