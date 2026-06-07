// Faithful port of the official Snowball Spanish stemming algorithm:
// https://snowballstem.org/algorithms/spanish/stemmer.html
// (Snowball 3.x spanish.sbl: RV/R1/R2 regions, step 0 attached pronouns, step 1 standard
// suffixes, steps 2a/2b verb suffixes, step 3 residual suffix, acute-accent postlude.)

using System.Text;
using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Es;

/// <summary>
/// Spanish Snowball stemmer. A pure managed port of the official Snowball Spanish algorithm
/// (see <c>https://snowballstem.org/algorithms/spanish/stemmer.html</c>).
/// Stateless and thread-safe: all working state lives in locals.
/// </summary>
public sealed class SnowballSpanishStemmer : IStemmer
{
    // Step 0: attached pronouns.
    private static readonly (string Suffix, int Group)[] PronounTable = ByLengthDescending(new[]
    {
        ("me", 0), ("se", 0), ("sela", 0), ("selo", 0), ("selas", 0), ("selos", 0),
        ("la", 0), ("le", 0), ("lo", 0), ("las", 0), ("les", 0), ("los", 0), ("nos", 0),
    });

    // Step 0: the verb form preceding the pronoun. Groups: 1..5 un-accent, 6 delete, 7 'yendo'.
    private static readonly (string Suffix, int Group)[] PronounStemTable = ByLengthDescending(new[]
    {
        ("iéndo", 1), ("ándo", 2), ("ár", 3), ("ér", 4), ("ír", 5),
        ("ando", 6), ("iendo", 6), ("ar", 6), ("er", 6), ("ir", 6),
        ("yendo", 7),
    });

    // Step 1: standard suffixes.
    private static readonly (string Suffix, int Group)[] Step1Table = ByLengthDescending(new[]
    {
        ("anza", 1), ("anzas", 1), ("ico", 1), ("ica", 1), ("icos", 1), ("icas", 1),
        ("ismo", 1), ("ismos", 1), ("able", 1), ("ables", 1), ("ible", 1), ("ibles", 1),
        ("ista", 1), ("istas", 1), ("oso", 1), ("osa", 1), ("osos", 1), ("osas", 1),
        ("amiento", 1), ("amientos", 1), ("imiento", 1), ("imientos", 1),
        ("adora", 2), ("ador", 2), ("ación", 2), ("adoras", 2), ("adores", 2), ("aciones", 2),
        ("ante", 2), ("antes", 2), ("ancia", 2), ("ancias", 2), ("acion", 2),
        ("logía", 3), ("logías", 3),
        ("ución", 4), ("uciones", 4), ("ucion", 4),
        ("encia", 5), ("encias", 5),
        ("amente", 6),
        ("mente", 7),
        ("idad", 8), ("idades", 8),
        ("iva", 9), ("ivo", 9), ("ivas", 9), ("ivos", 9),
    });

    // Step 2a: verb suffixes beginning y.
    private static readonly (string Suffix, int Group)[] YVerbTable = ByLengthDescending(new[]
    {
        ("ya", 0), ("ye", 0), ("yan", 0), ("yen", 0), ("yeron", 0), ("yendo", 0),
        ("yo", 0), ("yó", 0), ("yas", 0), ("yes", 0), ("yais", 0), ("yamos", 0),
    });

    // Step 2b: other verb suffixes. Group 1 may also remove a preceding gu-u.
    private static readonly (string Suffix, int Group)[] VerbTable = ByLengthDescending(new[]
    {
        ("en", 1), ("es", 1), ("éis", 1), ("emos", 1),
        ("arían", 2), ("arías", 2), ("arán", 2), ("arás", 2), ("aríais", 2), ("aría", 2),
        ("aréis", 2), ("aríamos", 2), ("aremos", 2), ("ará", 2), ("aré", 2),
        ("erían", 2), ("erías", 2), ("erán", 2), ("erás", 2), ("eríais", 2), ("ería", 2),
        ("eréis", 2), ("eríamos", 2), ("eremos", 2), ("erá", 2), ("eré", 2),
        ("irían", 2), ("irías", 2), ("irán", 2), ("irás", 2), ("iríais", 2), ("iría", 2),
        ("iréis", 2), ("iríamos", 2), ("iremos", 2), ("irá", 2), ("iré", 2),
        ("aba", 2), ("ada", 2), ("ida", 2), ("ía", 2), ("ara", 2), ("iera", 2),
        ("ad", 2), ("ed", 2), ("id", 2), ("ase", 2), ("iese", 2), ("aste", 2), ("iste", 2),
        ("an", 2), ("aban", 2), ("ían", 2), ("aran", 2), ("ieran", 2), ("asen", 2),
        ("iesen", 2), ("aron", 2), ("ieron", 2), ("ado", 2), ("ido", 2), ("ando", 2),
        ("iendo", 2), ("ió", 2), ("ar", 2), ("er", 2), ("ir", 2), ("as", 2),
        ("abas", 2), ("adas", 2), ("idas", 2), ("ías", 2), ("aras", 2), ("ieras", 2),
        ("ases", 2), ("ieses", 2), ("ís", 2), ("áis", 2), ("abais", 2), ("íais", 2),
        ("arais", 2), ("ierais", 2), ("aseis", 2), ("ieseis", 2), ("asteis", 2), ("isteis", 2),
        ("ados", 2), ("idos", 2), ("amos", 2), ("ábamos", 2), ("íamos", 2), ("imos", 2),
        ("áramos", 2), ("iéramos", 2), ("iésemos", 2), ("ásemos", 2),
    });

    // Step 3: residual suffixes.
    private static readonly (string Suffix, int Group)[] ResidualTable = ByLengthDescending(new[]
    {
        ("os", 1), ("a", 1), ("o", 1), ("á", 1), ("í", 1), ("ó", 1),
        ("e", 2), ("é", 2),
    });

    /// <inheritdoc />
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        var word = token;
        MarkRegions(word, out var rv, out var r1, out var r2);

        AttachedPronoun(ref word, rv);
        _ = StandardSuffix(ref word, r1, r2)
            || YVerbSuffix(ref word, rv)
            || VerbSuffix(ref word, rv);
        ResidualSuffix(ref word, rv);
        return Postlude(word);
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u'
        or 'á' or 'é' or 'í' or 'ó' or 'ú' or 'ü';

    private static void MarkRegions(string w, out int rv, out int r1, out int r2)
    {
        rv = ComputeRv(w);
        r1 = RegionAfterNonVowelFollowingVowel(w, 0);
        r2 = RegionAfterNonVowelFollowingVowel(w, r1);
    }

    private static int ComputeRv(string w)
    {
        var len = w.Length;
        if (len < 2)
        {
            return len;
        }

        if (!IsVowel(w[0]))
        {
            if (IsVowel(w[1]))
            {
                // Consonant-vowel: region after the third letter.
                return len >= 3 ? 3 : len;
            }

            return AfterFirst(w, 2, vowel: true);
        }

        if (!IsVowel(w[1]))
        {
            // Vowel-consonant: region after the next following vowel.
            return AfterFirst(w, 2, vowel: true);
        }

        // Two vowels: region after the next following consonant.
        return AfterFirst(w, 2, vowel: false);
    }

    private static int AfterFirst(string w, int start, bool vowel)
    {
        for (var i = start; i < w.Length; i++)
        {
            if (IsVowel(w[i]) == vowel)
            {
                return i + 1;
            }
        }

        return w.Length;
    }

    private static int RegionAfterNonVowelFollowingVowel(string w, int start)
    {
        var i = start;
        while (i < w.Length && !IsVowel(w[i]))
        {
            i++;
        }

        while (i < w.Length && IsVowel(w[i]))
        {
            i++;
        }

        return i < w.Length ? i + 1 : w.Length;
    }

    private static void AttachedPronoun(ref string w, int rv)
    {
        var pronounIndex = FindLongestSuffix(w, PronounTable, 0);
        if (pronounIndex < 0)
        {
            return;
        }

        var pronounStart = w.Length - PronounTable[pronounIndex].Suffix.Length;
        var head = w[..pronounStart];
        var stemIndex = FindLongestSuffix(head, PronounStemTable, 0);
        if (stemIndex < 0)
        {
            return;
        }

        var (stemSuffix, group) = PronounStemTable[stemIndex];
        var stemStart = head.Length - stemSuffix.Length;
        if (stemStart < rv)
        {
            return;
        }

        switch (group)
        {
            case 1:
                w = string.Concat(head.AsSpan(0, stemStart), "iendo");
                break;

            case 2:
                w = string.Concat(head.AsSpan(0, stemStart), "ando");
                break;

            case 3:
                w = string.Concat(head.AsSpan(0, stemStart), "ar");
                break;

            case 4:
                w = string.Concat(head.AsSpan(0, stemStart), "er");
                break;

            case 5:
                w = string.Concat(head.AsSpan(0, stemStart), "ir");
                break;

            case 6:
                w = head;
                break;

            default:
                // yendo: delete the pronoun only when preceded by u.
                if (stemStart >= 1 && head[stemStart - 1] == 'u')
                {
                    w = head;
                }

                break;
        }
    }

    private static bool StandardSuffix(ref string w, int r1, int r2)
    {
        var index = FindLongestSuffix(w, Step1Table, 0);
        if (index < 0)
        {
            return false;
        }

        var (suffix, group) = Step1Table[index];
        var start = w.Length - suffix.Length;
        switch (group)
        {
            case 1:
                if (start < r2)
                {
                    return false;
                }

                w = w[..start];
                return true;

            case 2:
                if (start < r2)
                {
                    return false;
                }

                w = w[..start];
                if (w.EndsWith("ic", StringComparison.Ordinal) && w.Length - 2 >= r2)
                {
                    w = w[..^2];
                }

                return true;

            case 3:
                if (start < r2)
                {
                    return false;
                }

                w = string.Concat(w.AsSpan(0, start), "log");
                return true;

            case 4:
                if (start < r2)
                {
                    return false;
                }

                w = string.Concat(w.AsSpan(0, start), "u");
                return true;

            case 5:
                if (start < r2)
                {
                    return false;
                }

                w = string.Concat(w.AsSpan(0, start), "ente");
                return true;

            case 6:
                if (start < r1)
                {
                    return false;
                }

                w = w[..start];
                AmenteFollowUp(ref w, r2);
                return true;

            case 7:
                if (start < r2)
                {
                    return false;
                }

                w = w[..start];
                if ((w.EndsWith("ante", StringComparison.Ordinal)
                        || w.EndsWith("able", StringComparison.Ordinal)
                        || w.EndsWith("ible", StringComparison.Ordinal))
                    && w.Length - 4 >= r2)
                {
                    w = w[..^4];
                }

                return true;

            case 8:
                if (start < r2)
                {
                    return false;
                }

                w = w[..start];
                IdadFollowUp(ref w, r2);
                return true;

            default:
                if (start < r2)
                {
                    return false;
                }

                w = w[..start];
                if (w.EndsWith("at", StringComparison.Ordinal) && w.Length - 2 >= r2)
                {
                    w = w[..^2];
                }

                return true;
        }
    }

    private static void AmenteFollowUp(ref string w, int r2)
    {
        // Longest of iv/os/ic/ad (all length 2); if in R2 delete; for iv also try a preceding at.
        if (w.Length < 2)
        {
            return;
        }

        var start = w.Length - 2;
        if (start < r2)
        {
            return;
        }

        if (w.EndsWith("iv", StringComparison.Ordinal))
        {
            w = w[..start];
            if (w.EndsWith("at", StringComparison.Ordinal) && w.Length - 2 >= r2)
            {
                w = w[..^2];
            }
        }
        else if (w.EndsWith("os", StringComparison.Ordinal)
            || w.EndsWith("ic", StringComparison.Ordinal)
            || w.EndsWith("ad", StringComparison.Ordinal))
        {
            w = w[..start];
        }
    }

    private static void IdadFollowUp(ref string w, int r2)
    {
        if (w.EndsWith("abil", StringComparison.Ordinal))
        {
            if (w.Length - 4 >= r2)
            {
                w = w[..^4];
            }
        }
        else if (w.EndsWith("ic", StringComparison.Ordinal) || w.EndsWith("iv", StringComparison.Ordinal))
        {
            if (w.Length - 2 >= r2)
            {
                w = w[..^2];
            }
        }
    }

    private static bool YVerbSuffix(ref string w, int rv)
    {
        var index = FindLongestSuffix(w, YVerbTable, rv);
        if (index < 0)
        {
            return false;
        }

        var start = w.Length - YVerbTable[index].Suffix.Length;

        // Delete if preceded by u (the u need not be in RV).
        if (start < 1 || w[start - 1] != 'u')
        {
            return false;
        }

        w = w[..start];
        return true;
    }

    private static bool VerbSuffix(ref string w, int rv)
    {
        var index = FindLongestSuffix(w, VerbTable, rv);
        if (index < 0)
        {
            return false;
        }

        var (suffix, group) = VerbTable[index];
        var start = w.Length - suffix.Length;
        if (group == 1 && start >= 2 && w[start - 1] == 'u' && w[start - 2] == 'g')
        {
            // Delete a u after g as well (the gu need not be in RV).
            start--;
        }

        w = w[..start];
        return true;
    }

    private static void ResidualSuffix(ref string w, int rv)
    {
        var index = FindLongestSuffix(w, ResidualTable, 0);
        if (index < 0)
        {
            return;
        }

        var (suffix, group) = ResidualTable[index];
        var start = w.Length - suffix.Length;
        if (start < rv)
        {
            return;
        }

        w = w[..start];
        if (group == 2)
        {
            // After deleting e/é: also remove a final u after g when the u is in RV.
            var p = w.Length - 1;
            if (p >= 1 && w[p] == 'u' && w[p - 1] == 'g' && p >= rv)
            {
                w = w[..p];
            }
        }
    }

    private static string Postlude(string w)
    {
        var sb = new StringBuilder(w.Length);
        foreach (var c in w)
        {
            sb.Append(c switch
            {
                'á' => 'a',
                'é' => 'e',
                'í' => 'i',
                'ó' => 'o',
                'ú' => 'u',
                _ => c,
            });
        }

        return sb.ToString();
    }

    private static int FindLongestSuffix(string word, (string Suffix, int Group)[] table, int minStart)
    {
        for (var i = 0; i < table.Length; i++)
        {
            var suffix = table[i].Suffix;
            var start = word.Length - suffix.Length;
            if (start >= minStart && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static (string Suffix, int Group)[] ByLengthDescending((string, int)[] rules)
        => rules.OrderByDescending(static r => r.Item1.Length).ToArray();
}
