// Faithful port of the official Snowball Portuguese stemming algorithm:
// https://snowballstem.org/algorithms/portuguese/stemmer.html
// (Snowball 3.x portuguese.sbl: ã/õ -> a~/o~ transliteration, RV/R1/R2, standard suffixes,
// verb suffixes, residual suffix and residual form steps, postlude back-transliteration.)

using System.Text;
using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Pt;

/// <summary>
/// Portuguese Snowball stemmer. A pure managed port of the official Snowball Portuguese
/// algorithm (see <c>https://snowballstem.org/algorithms/portuguese/stemmer.html</c>).
/// Stateless and thread-safe: all working state lives in locals.
/// </summary>
public sealed class SnowballPortugueseStemmer : IStemmer
{
    // Standard suffixes (on the a~/o~ transliterated form).
    private static readonly (string Suffix, int Group)[] Step1Table = ByLengthDescending(new[]
    {
        ("eza", 1), ("ezas", 1), ("ico", 1), ("ica", 1), ("icos", 1), ("icas", 1),
        ("ismo", 1), ("ismos", 1), ("ável", 1), ("ível", 1), ("ista", 1), ("istas", 1),
        ("oso", 1), ("osa", 1), ("osos", 1), ("osas", 1),
        ("amento", 1), ("amentos", 1), ("imento", 1), ("imentos", 1),
        ("adora", 1), ("ador", 1), ("aça~o", 1), ("adoras", 1), ("adores", 1), ("aço~es", 1),
        ("ante", 1), ("antes", 1), ("ância", 1),
        ("logia", 2), ("logias", 2),
        ("uça~o", 3), ("uço~es", 3),
        ("ência", 4), ("ências", 4),
        ("amente", 5),
        ("mente", 6),
        ("idade", 7), ("idades", 7),
        ("iva", 8), ("ivo", 8), ("ivas", 8), ("ivos", 8),
        ("ira", 9), ("iras", 9),
    });

    // Verb suffixes; the whole step is confined to RV.
    private static readonly (string Suffix, int Group)[] VerbTable = ByLengthDescending(new[]
    {
        ("ada", 0), ("ida", 0), ("ia", 0), ("aria", 0), ("eria", 0), ("iria", 0),
        ("ará", 0), ("ara", 0), ("erá", 0), ("era", 0), ("irá", 0), ("ava", 0),
        ("asse", 0), ("esse", 0), ("isse", 0), ("aste", 0), ("este", 0), ("iste", 0),
        ("ei", 0), ("arei", 0), ("erei", 0), ("irei", 0), ("am", 0), ("iam", 0),
        ("ariam", 0), ("eriam", 0), ("iriam", 0), ("aram", 0), ("eram", 0), ("iram", 0),
        ("avam", 0), ("em", 0), ("arem", 0), ("erem", 0), ("irem", 0),
        ("assem", 0), ("essem", 0), ("issem", 0), ("ado", 0), ("ido", 0),
        ("ando", 0), ("endo", 0), ("indo", 0), ("ara~o", 0), ("era~o", 0), ("ira~o", 0),
        ("ar", 0), ("er", 0), ("ir", 0), ("as", 0), ("adas", 0), ("idas", 0), ("ias", 0),
        ("arias", 0), ("erias", 0), ("irias", 0), ("arás", 0), ("aras", 0), ("erás", 0),
        ("eras", 0), ("irás", 0), ("avas", 0), ("es", 0), ("ardes", 0), ("erdes", 0),
        ("irdes", 0), ("ares", 0), ("eres", 0), ("ires", 0), ("asses", 0), ("esses", 0),
        ("isses", 0), ("astes", 0), ("estes", 0), ("istes", 0), ("is", 0), ("ais", 0),
        ("eis", 0), ("íeis", 0), ("aríeis", 0), ("eríeis", 0), ("iríeis", 0),
        ("áreis", 0), ("areis", 0), ("éreis", 0), ("ereis", 0), ("íreis", 0), ("ireis", 0),
        ("ásseis", 0), ("ésseis", 0), ("ísseis", 0), ("áveis", 0), ("ados", 0), ("idos", 0),
        ("ámos", 0), ("amos", 0), ("íamos", 0), ("aríamos", 0), ("eríamos", 0), ("iríamos", 0),
        ("áramos", 0), ("éramos", 0), ("íramos", 0), ("ávamos", 0),
        ("emos", 0), ("aremos", 0), ("eremos", 0), ("iremos", 0),
        ("ássemos", 0), ("êssemos", 0), ("íssemos", 0), ("imos", 0),
        ("armos", 0), ("ermos", 0), ("irmos", 0), ("eu", 0), ("iu", 0), ("ou", 0),
        ("ira", 0), ("iras", 0),
    });

    // Residual suffixes.
    private static readonly (string Suffix, int Group)[] ResidualTable = ByLengthDescending(new[]
    {
        ("os", 0), ("a", 0), ("i", 0), ("o", 0), ("á", 0), ("í", 0), ("ó", 0),
    });

    /// <inheritdoc />
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        var word = Prelude(token);
        MarkRegions(word, out var rv, out var r1, out var r2);

        if (StandardSuffix(ref word, rv, r1, r2) || VerbSuffix(ref word, rv))
        {
            // do ( ['i'] test 'c' RV delete )
            var p = word.Length - 1;
            if (p >= 1 && word[p] == 'i' && word[p - 1] == 'c' && p >= rv)
            {
                word = word[..p];
            }
        }
        else
        {
            ResidualSuffix(ref word, rv);
        }

        ResidualForm(ref word, rv);
        return Postlude(word);
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u'
        or 'á' or 'é' or 'í' or 'ó' or 'ú' or 'â' or 'ê' or 'ô';

    private static string Prelude(string w)
    {
        var sb = new StringBuilder(w.Length + 2);
        foreach (var c in w)
        {
            switch (c)
            {
                case 'ã':
                    sb.Append("a~");
                    break;

                case 'õ':
                    sb.Append("o~");
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

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
                return len >= 3 ? 3 : len;
            }

            return AfterFirst(w, 2, vowel: true);
        }

        if (!IsVowel(w[1]))
        {
            return AfterFirst(w, 2, vowel: true);
        }

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

    private static bool StandardSuffix(ref string w, int rv, int r1, int r2)
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

                w = string.Concat(w.AsSpan(0, start), "log");
                return true;

            case 3:
                if (start < r2)
                {
                    return false;
                }

                w = string.Concat(w.AsSpan(0, start), "u");
                return true;

            case 4:
                if (start < r2)
                {
                    return false;
                }

                w = string.Concat(w.AsSpan(0, start), "ente");
                return true;

            case 5:
                if (start < r1)
                {
                    return false;
                }

                w = w[..start];
                AmenteFollowUp(ref w, r2);
                return true;

            case 6:
                if (start < r2)
                {
                    return false;
                }

                w = w[..start];
                if ((w.EndsWith("ante", StringComparison.Ordinal)
                        || w.EndsWith("avel", StringComparison.Ordinal)
                        || w.EndsWith("ível", StringComparison.Ordinal))
                    && w.Length - 4 >= r2)
                {
                    w = w[..^4];
                }

                return true;

            case 7:
                if (start < r2)
                {
                    return false;
                }

                w = w[..start];
                if (w.EndsWith("abil", StringComparison.Ordinal))
                {
                    if (w.Length - 4 >= r2)
                    {
                        w = w[..^4];
                    }
                }
                else if ((w.EndsWith("ic", StringComparison.Ordinal) || w.EndsWith("iv", StringComparison.Ordinal))
                    && w.Length - 2 >= r2)
                {
                    w = w[..^2];
                }

                return true;

            case 8:
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

            default:
                // ira/iras: usually non-verbal after e (-eira/-eiras); replace with ir.
                if (start < rv || start < 1 || w[start - 1] != 'e')
                {
                    return false;
                }

                w = string.Concat(w.AsSpan(0, start), "ir");
                return true;
        }
    }

    private static void AmenteFollowUp(ref string w, int r2)
    {
        // Longest of iv/os/ic/ad (all length 2); delete if in R2; for iv also try a preceding at.
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

    private static bool VerbSuffix(ref string w, int rv)
    {
        var index = FindLongestSuffix(w, VerbTable, rv);
        if (index < 0)
        {
            return false;
        }

        w = w[..(w.Length - VerbTable[index].Suffix.Length)];
        return true;
    }

    private static void ResidualSuffix(ref string w, int rv)
    {
        var index = FindLongestSuffix(w, ResidualTable, 0);
        if (index < 0)
        {
            return;
        }

        var start = w.Length - ResidualTable[index].Suffix.Length;
        if (start >= rv)
        {
            w = w[..start];
        }
    }

    private static void ResidualForm(ref string w, int rv)
    {
        if (w.EndsWith('e') || w.EndsWith('é') || w.EndsWith('ê'))
        {
            var start = w.Length - 1;
            if (start < rv)
            {
                return;
            }

            w = w[..start];

            // Also remove a final u after g or i after c when in RV.
            var p = w.Length - 1;
            if (p >= 1 && ((w[p] == 'u' && w[p - 1] == 'g') || (w[p] == 'i' && w[p - 1] == 'c')) && p >= rv)
            {
                w = w[..p];
            }

            return;
        }

        if (w.EndsWith('ç'))
        {
            w = string.Concat(w.AsSpan(0, w.Length - 1), "c");
        }
    }

    private static string Postlude(string w)
    {
        var sb = new StringBuilder(w.Length);
        for (var i = 0; i < w.Length; i++)
        {
            var c = w[i];
            if (c is 'a' or 'o' && i + 1 < w.Length && w[i + 1] == '~')
            {
                sb.Append(c == 'a' ? 'ã' : 'õ');
                i++;
            }
            else
            {
                sb.Append(c);
            }
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
