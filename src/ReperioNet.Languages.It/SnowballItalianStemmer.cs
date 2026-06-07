// Faithful port of the official Snowball Italian stemming algorithm:
// https://snowballstem.org/algorithms/italian/stemmer.html
// (Snowball 3.x italian.sbl: elisions, prelude with acute->grave normalization, qU and U/I
// marking, RV/R1/R2, attached pronouns, standard suffixes, verb suffixes, vowel suffixes.)

using System.Text;
using ReperioNet.Abstractions;

namespace ReperioNet.Languages.It;

/// <summary>
/// Italian Snowball stemmer. A pure managed port of the official Snowball Italian algorithm
/// (see <c>https://snowballstem.org/algorithms/italian/stemmer.html</c>).
/// Stateless and thread-safe: all working state lives in locals.
/// </summary>
public sealed class SnowballItalianStemmer : IStemmer
{
    // Elided article/pronoun prefixes (with apostrophe).
    private static readonly (string Suffix, int Group)[] ElisionTable = ByLengthDescending(new[]
    {
        ("d'", 0), ("l'", 0), ("m'", 0), ("s'", 0), ("t'", 0), ("v'", 0),
        ("all'", 0), ("dall'", 0), ("dell'", 0), ("gl'", 0), ("nell'", 0),
        ("quell'", 0), ("quest'", 0), ("sull'", 0), ("tutt'", 0), ("un'", 0),
    });

    // Attached pronouns.
    private static readonly (string Suffix, int Group)[] PronounTable = ByLengthDescending(new[]
    {
        ("ci", 0), ("gli", 0), ("la", 0), ("le", 0), ("li", 0), ("lo", 0),
        ("mi", 0), ("ne", 0), ("si", 0), ("ti", 0), ("vi", 0),
        ("sene", 0), ("gliela", 0), ("gliele", 0), ("glieli", 0), ("glielo", 0), ("gliene", 0),
        ("mela", 0), ("mele", 0), ("meli", 0), ("melo", 0), ("mene", 0),
        ("tela", 0), ("tele", 0), ("teli", 0), ("telo", 0), ("tene", 0),
        ("cela", 0), ("cele", 0), ("celi", 0), ("celo", 0), ("cene", 0),
        ("vela", 0), ("vele", 0), ("veli", 0), ("velo", 0), ("vene", 0),
    });

    // Verb forms preceding an attached pronoun: group 1 delete, group 2 replace with e.
    private static readonly (string Suffix, int Group)[] PronounStemTable = ByLengthDescending(new[]
    {
        ("ando", 1), ("endo", 1),
        ("ar", 2), ("er", 2), ("ir", 2),
    });

    // Standard suffixes.
    private static readonly (string Suffix, int Group)[] Step1Table = ByLengthDescending(new[]
    {
        ("anza", 1), ("anze", 1), ("ico", 1), ("ici", 1), ("ica", 1), ("ice", 1),
        ("iche", 1), ("ichi", 1), ("ismo", 1), ("ismi", 1), ("abile", 1), ("abili", 1),
        ("ibile", 1), ("ibili", 1), ("ista", 1), ("iste", 1), ("isti", 1),
        ("istà", 1), ("istè", 1), ("istì", 1), ("oso", 1), ("osi", 1), ("osa", 1), ("ose", 1),
        ("mente", 1), ("atrice", 1), ("atrici", 1), ("ante", 1), ("anti", 1),
        ("azione", 2), ("azioni", 2), ("atore", 2), ("atori", 2),
        ("logia", 3), ("logie", 3),
        ("uzione", 4), ("uzioni", 4), ("usione", 4), ("usioni", 4),
        ("enza", 5), ("enze", 5),
        ("amento", 6), ("amenti", 6), ("imento", 6), ("imenti", 6),
        ("amente", 7),
        ("ità", 8),
        ("ivo", 9), ("ivi", 9), ("iva", 9), ("ive", 9),
    });

    // Verb suffixes; the whole step is confined to RV.
    private static readonly (string Suffix, int Group)[] VerbTable = ByLengthDescending(new[]
    {
        ("ammo", 0), ("ando", 0), ("ano", 0), ("are", 0), ("arono", 0), ("asse", 0),
        ("assero", 0), ("assi", 0), ("assimo", 0), ("ata", 0), ("ate", 0), ("ati", 0),
        ("ato", 0), ("ava", 0), ("avamo", 0), ("avano", 0), ("avate", 0), ("avi", 0),
        ("avo", 0), ("emmo", 0), ("enda", 0), ("ende", 0), ("endi", 0), ("endo", 0),
        ("erà", 0), ("erai", 0), ("eranno", 0), ("ere", 0), ("erebbe", 0), ("erebbero", 0),
        ("erei", 0), ("eremmo", 0), ("eremo", 0), ("ereste", 0), ("eresti", 0), ("erete", 0),
        ("erò", 0), ("erono", 0), ("essero", 0), ("ete", 0), ("eva", 0), ("evamo", 0),
        ("evano", 0), ("evate", 0), ("evi", 0), ("evo", 0), ("Yamo", 0), ("iamo", 0),
        ("immo", 0), ("irà", 0), ("irai", 0), ("iranno", 0), ("ire", 0), ("irebbe", 0),
        ("irebbero", 0), ("irei", 0), ("iremmo", 0), ("iremo", 0), ("ireste", 0), ("iresti", 0),
        ("irete", 0), ("irò", 0), ("irono", 0), ("isca", 0), ("iscano", 0), ("isce", 0),
        ("isci", 0), ("isco", 0), ("iscono", 0), ("issero", 0), ("ita", 0), ("ite", 0),
        ("iti", 0), ("ito", 0), ("iva", 0), ("ivamo", 0), ("ivano", 0), ("ivate", 0),
        ("ivi", 0), ("ivo", 0), ("ono", 0), ("uta", 0), ("ute", 0), ("uti", 0), ("uto", 0),
        ("ar", 0), ("ir", 0),
    });

    /// <inheritdoc />
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        var word = RemoveElision(token);
        word = Prelude(word);
        MarkRegions(word, out var rv, out var r1, out var r2);

        AttachedPronoun(ref word, rv);
        _ = StandardSuffix(ref word, rv, r1, r2) || VerbSuffix(ref word, rv);
        VowelSuffix(ref word, rv);
        return Postlude(word);
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u'
        or 'à' or 'è' or 'ì' or 'ò' or 'ù';

    private static string RemoveElision(string w)
    {
        var index = FindLongestPrefix(w, ElisionTable);
        if (index < 0)
        {
            return w;
        }

        var prefix = ElisionTable[index].Suffix;
        return prefix.Length < w.Length ? w[prefix.Length..] : w;
    }

    private static string Prelude(string w)
    {
        // First pass: acute -> grave, qu -> qU.
        var sb = new StringBuilder(w.Length);
        for (var i = 0; i < w.Length; i++)
        {
            var c = w[i];
            switch (c)
            {
                case 'á':
                    sb.Append('à');
                    break;

                case 'é':
                    sb.Append('è');
                    break;

                case 'í':
                    sb.Append('ì');
                    break;

                case 'ó':
                    sb.Append('ò');
                    break;

                case 'ú':
                    sb.Append('ù');
                    break;

                case 'q' when i + 1 < w.Length && w[i + 1] == 'u':
                    sb.Append('q').Append('U');
                    i++;
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        // Second pass: mark u/i between vowels as U/I.
        var i2 = 0;
        while (i2 < sb.Length)
        {
            if (IsVowel(sb[i2]) && i2 + 2 < sb.Length && (sb[i2 + 1] == 'u' || sb[i2 + 1] == 'i')
                && IsVowel(sb[i2 + 2]))
            {
                // The trailing vowel can anchor the next match (Snowball goto re-scans).
                sb[i2 + 1] = sb[i2 + 1] == 'u' ? 'U' : 'I';
                i2 += 2;
            }
            else
            {
                i2++;
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
            // Exception: keep "divano" from colliding with "diva".
            if (w.StartsWith("divan", StringComparison.Ordinal))
            {
                return 5;
            }

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
        if (head.Length - stemSuffix.Length < rv)
        {
            return;
        }

        // Group 1 (ando/endo): drop the pronoun; group 2 (ar/er/ir): replace it with e.
        w = group == 1 ? head : head + "e";
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
                if (start < rv)
                {
                    return false;
                }

                w = w[..start];
                return true;

            case 7:
                if (start < r1)
                {
                    return false;
                }

                w = w[..start];
                AmenteFollowUp(ref w, r2);
                return true;

            case 8:
                if (start < r2)
                {
                    return false;
                }

                w = w[..start];
                if ((w.EndsWith("abil", StringComparison.Ordinal) && w.Length - 4 >= r2)
                    || ((w.EndsWith("ic", StringComparison.Ordinal) || w.EndsWith("iv", StringComparison.Ordinal))
                        && w.Length - 2 >= r2))
                {
                    w = w.EndsWith("abil", StringComparison.Ordinal) ? w[..^4] : w[..^2];
                }

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
                    if (w.EndsWith("ic", StringComparison.Ordinal) && w.Length - 2 >= r2)
                    {
                        w = w[..^2];
                    }
                }

                return true;
        }
    }

    private static void AmenteFollowUp(ref string w, int r2)
    {
        // Longest of abil(4)/iv/os/ic(2); delete if in R2; for iv also try a preceding at.
        if (w.EndsWith("abil", StringComparison.Ordinal))
        {
            if (w.Length - 4 >= r2)
            {
                w = w[..^4];
            }

            return;
        }

        if (w.Length < 2 || w.Length - 2 < r2)
        {
            return;
        }

        if (w.EndsWith("iv", StringComparison.Ordinal))
        {
            w = w[..^2];
            if (w.EndsWith("at", StringComparison.Ordinal) && w.Length - 2 >= r2)
            {
                w = w[..^2];
            }
        }
        else if (w.EndsWith("os", StringComparison.Ordinal) || w.EndsWith("ic", StringComparison.Ordinal))
        {
            w = w[..^2];
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

    private static void VowelSuffix(ref string w, int rv)
    {
        // Final a/e/i/o/à/è/ì/ò in RV, then a further final i in RV.
        var p = w.Length - 1;
        if (p >= 0 && p >= rv && w[p] is 'a' or 'e' or 'i' or 'o' or 'à' or 'è' or 'ì' or 'ò')
        {
            w = w[..p];
            p = w.Length - 1;
            if (p >= 0 && p >= rv && w[p] == 'i')
            {
                w = w[..p];
            }
        }

        // Final h after c/g in RV.
        p = w.Length - 1;
        if (p >= 1 && w[p] == 'h' && w[p - 1] is 'c' or 'g' && p - 1 >= rv)
        {
            w = w[..p];
        }
    }

    private static string Postlude(string w)
    {
        var sb = new StringBuilder(w.Length);
        foreach (var c in w)
        {
            sb.Append(c switch
            {
                'I' => 'i',
                'U' => 'u',
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

    private static int FindLongestPrefix(string word, (string Suffix, int Group)[] table)
    {
        for (var i = 0; i < table.Length; i++)
        {
            if (word.StartsWith(table[i].Suffix, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static (string Suffix, int Group)[] ByLengthDescending((string, int)[] rules)
        => rules.OrderByDescending(static r => r.Item1.Length).ToArray();
}
