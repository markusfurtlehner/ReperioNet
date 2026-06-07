// Faithful port of the official Snowball French stemming algorithm:
// https://snowballstem.org/algorithms/french/stemmer.html
// (Snowball 3.x french.sbl: elisions, prelude with U/I/Y/qU/He/Hi marking, RV/R1/R2,
// standard suffixes, verb suffix steps, residual step, undouble, un-accent, postlude.)

using System.Text;
using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Fr;

/// <summary>
/// French Snowball stemmer. A pure managed, allocation-only port of the official Snowball
/// French algorithm (see <c>https://snowballstem.org/algorithms/french/stemmer.html</c>).
/// Stateless and thread-safe: all working state lives in locals.
/// </summary>
public sealed class SnowballFrenchStemmer : IStemmer
{
    // Step 1 (standard suffix) table; matched longest-first.
    private static readonly (string Suffix, int Group)[] Step1Table = ByLengthDescending(new[]
    {
        ("ance", 1), ("iqUe", 1), ("isme", 1), ("able", 1), ("iste", 1), ("eux", 1),
        ("ances", 1), ("iqUes", 1), ("ismes", 1), ("ables", 1), ("istes", 1),
        ("atrice", 2), ("ateur", 2), ("ation", 2), ("atrices", 2), ("ateurs", 2), ("ations", 2),
        ("logie", 3), ("logies", 3),
        ("usion", 4), ("ution", 4), ("usions", 4), ("utions", 4),
        ("ence", 5), ("ences", 5),
        ("ement", 6), ("ements", 6),
        ("ité", 7), ("ités", 7),
        ("if", 8), ("ive", 8), ("ifs", 8), ("ives", 8),
        ("eaux", 9),
        ("aux", 10),
        ("oux", 11),
        ("euse", 12), ("euses", 12),
        ("issement", 13), ("issements", 13),
        ("amment", 14),
        ("emment", 15),
        ("ment", 16), ("ments", 16),
    });

    // Step 2a (i-verb suffixes); the whole step is confined to RV.
    private static readonly (string Suffix, int Group)[] IVerbTable = ByLengthDescending(new[]
    {
        ("îmes", 0), ("ît", 0), ("îtes", 0), ("i", 0), ("ie", 0), ("ies", 0), ("ir", 0),
        ("ira", 0), ("irai", 0), ("iraIent", 0), ("irais", 0), ("irait", 0), ("iras", 0),
        ("irent", 0), ("irez", 0), ("iriez", 0), ("irions", 0), ("irons", 0), ("iront", 0),
        ("is", 0), ("issaIent", 0), ("issais", 0), ("issait", 0), ("issant", 0), ("issante", 0),
        ("issantes", 0), ("issants", 0), ("isse", 0), ("issent", 0), ("isses", 0), ("issez", 0),
        ("issiez", 0), ("issions", 0), ("issons", 0), ("it", 0),
    });

    // Step 2b (other verb suffixes); suffix match confined to RV, conditions are not.
    private static readonly (string Suffix, int Group)[] VerbTable = ByLengthDescending(new[]
    {
        ("ions", 1),
        ("é", 2), ("ée", 2), ("ées", 2), ("és", 2), ("èrent", 2), ("er", 2), ("era", 2),
        ("erai", 2), ("eraIent", 2), ("erais", 2), ("erait", 2), ("eras", 2), ("erez", 2),
        ("eriez", 2), ("erions", 2), ("erons", 2), ("eront", 2), ("ez", 2), ("iez", 2),
        ("âmes", 3), ("ât", 3), ("âtes", 3), ("a", 3), ("ai", 3), ("aIent", 3), ("ait", 3),
        ("ant", 3), ("ante", 3), ("antes", 3), ("ants", 3), ("as", 3), ("asse", 3),
        ("assent", 3), ("asses", 3), ("assiez", 3), ("assions", 3),
        ("ais", 4), ("aise", 4), ("aises", 4),
        ("eais", 5),
    });

    // Step 1 -ement follow-up table.
    private static readonly (string Suffix, int Group)[] EmentTable = ByLengthDescending(new[]
    {
        ("iv", 1),
        ("eus", 2),
        ("abl", 3), ("iqU", 3),
        ("ièr", 4), ("Ièr", 4),
    });

    // Step 1 -ité follow-up table.
    private static readonly (string Suffix, int Group)[] IteTable = ByLengthDescending(new[]
    {
        ("abil", 1),
        ("ic", 2),
        ("iv", 3),
    });

    // Residual suffix table; confined to RV.
    private static readonly (string Suffix, int Group)[] ResidualTable = ByLengthDescending(new[]
    {
        ("ion", 1),
        ("ier", 2), ("ière", 2), ("Ier", 2), ("Ière", 2),
        ("e", 3),
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

        var changed = StandardSuffix(ref word, rv, r1, r2)
            || IVerbSuffix(ref word, rv)
            || VerbSuffix(ref word, rv, r2);

        if (changed)
        {
            // Replace final Y with i or final ç with c.
            if (word.EndsWith('Y'))
            {
                word = string.Concat(word.AsSpan(0, word.Length - 1), "i");
            }
            else if (word.EndsWith('ç'))
            {
                word = string.Concat(word.AsSpan(0, word.Length - 1), "c");
            }
        }
        else
        {
            ResidualSuffix(ref word, rv, r2);
        }

        UnDouble(ref word);
        UnAccent(ref word);
        return Postlude(word);
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y'
        or 'â' or 'à' or 'ë' or 'é' or 'ê' or 'è' or 'ï' or 'î' or 'ô' or 'û' or 'ù';

    private static string RemoveElision(string w)
    {
        if (w.Length >= 3 && w[1] == '\'' && w[0] is 'c' or 'd' or 'j' or 'l' or 'm' or 'n' or 's' or 't')
        {
            return w[2..];
        }

        if (w.Length >= 4 && w[0] == 'q' && w[1] == 'u' && w[2] == '\'')
        {
            return w[3..];
        }

        return w;
    }

    private static string Prelude(string w)
    {
        var sb = new StringBuilder(w);
        var i = 0;
        while (i < sb.Length)
        {
            var c = sb[i];
            if (IsVowel(c) && i + 1 < sb.Length)
            {
                var n = sb[i + 1];
                if ((n == 'u' || n == 'i') && i + 2 < sb.Length && IsVowel(sb[i + 2]))
                {
                    // The trailing vowel can anchor the next match (Snowball goto re-scans).
                    sb[i + 1] = n == 'u' ? 'U' : 'I';
                    i += 2;
                    continue;
                }

                if (n == 'y')
                {
                    sb[i + 1] = 'Y';
                    i += 2;
                    continue;
                }
            }

            if (c == 'ë')
            {
                // The inserted e can anchor the next match.
                sb.Remove(i, 1).Insert(i, "He");
                i += 1;
                continue;
            }

            if (c == 'ï')
            {
                // The inserted i can anchor the next match.
                sb.Remove(i, 1).Insert(i, "Hi");
                i += 1;
                continue;
            }

            if (c == 'y' && i + 1 < sb.Length && IsVowel(sb[i + 1]))
            {
                // The following vowel can anchor the next match.
                sb[i] = 'Y';
                i += 1;
                continue;
            }

            if (c == 'q' && i + 1 < sb.Length && sb[i + 1] == 'u')
            {
                sb[i + 1] = 'U';
                i += 2;
                continue;
            }

            i++;
        }

        return sb.ToString();
    }

    private static void MarkRegions(string w, out int rv, out int r1, out int r2)
    {
        var len = w.Length;
        rv = len;

        if (len >= 3 && IsVowel(w[0]) && IsVowel(w[1]))
        {
            // Word starts with two vowels: RV is the region after the third letter.
            rv = 3;
        }
        else if (len >= 3
            && (StartsWith3(w, 'p', 'a', 'r') || StartsWith3(w, 'c', 'o', 'l') || StartsWith3(w, 't', 'a', 'p')
                || (w[0] == 'n' && w[1] == 'i' && IsVowel(w[2]))))
        {
            // Exception list: par-, col-, tap-, ni+vowel.
            rv = 3;
        }
        else
        {
            // Region after the first vowel not at the beginning of the word.
            for (var i = 1; i < len; i++)
            {
                if (IsVowel(w[i]))
                {
                    rv = i + 1;
                    break;
                }
            }
        }

        r1 = RegionAfterNonVowelFollowingVowel(w, 0);
        r2 = RegionAfterNonVowelFollowingVowel(w, r1);
    }

    private static bool StartsWith3(string w, char a, char b, char c) => w[0] == a && w[1] == b && w[2] == c;

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

                w = w[..start];
                if (w.EndsWith("ic", StringComparison.Ordinal))
                {
                    var icStart = w.Length - 2;
                    w = icStart >= r2 ? w[..icStart] : string.Concat(w.AsSpan(0, icStart), "iqU");
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

                w = string.Concat(w.AsSpan(0, start), "ent");
                return true;

            case 6:
                if (start < rv)
                {
                    return false;
                }

                w = w[..start];
                EmentFollowUp(ref w, rv, r1, r2);
                return true;

            case 7:
                if (start < r2)
                {
                    return false;
                }

                w = w[..start];
                IteFollowUp(ref w, r2);
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
                    if (w.EndsWith("ic", StringComparison.Ordinal))
                    {
                        var icStart = w.Length - 2;
                        w = icStart >= r2 ? w[..icStart] : string.Concat(w.AsSpan(0, icStart), "iqU");
                    }
                }

                return true;

            case 9:
                w = string.Concat(w.AsSpan(0, start), "eau");
                return true;

            case 10:
                if (start < r1)
                {
                    return false;
                }

                w = string.Concat(w.AsSpan(0, start), "al");
                return true;

            case 11:
                if (start < 1 || w[start - 1] is not ('b' or 'h' or 'j' or 'l' or 'n' or 'p'))
                {
                    return false;
                }

                w = string.Concat(w.AsSpan(0, start), "ou");
                return true;

            case 12:
                if (start >= r2)
                {
                    w = w[..start];
                    return true;
                }

                if (start >= r1)
                {
                    w = string.Concat(w.AsSpan(0, start), "eux");
                    return true;
                }

                return false;

            case 13:
                if (start < r1 || start < 1 || IsVowel(w[start - 1]))
                {
                    return false;
                }

                w = w[..start];
                return true;

            case 14:
                // amment -> ant, then force the verb suffix steps (Snowball "fail").
                if (start >= rv)
                {
                    w = string.Concat(w.AsSpan(0, start), "ant");
                }

                return false;

            case 15:
                // emment -> ent, then force the verb suffix steps (Snowball "fail").
                if (start >= rv)
                {
                    w = string.Concat(w.AsSpan(0, start), "ent");
                }

                return false;

            default:
                // ment/ments: delete when preceded by a vowel in RV, then force the verb steps.
                if (start >= 1 && IsVowel(w[start - 1]) && start - 1 >= rv)
                {
                    w = w[..start];
                }

                return false;
        }
    }

    private static void EmentFollowUp(ref string w, int rv, int r1, int r2)
    {
        var index = FindLongestSuffix(w, EmentTable, 0);
        if (index < 0)
        {
            return;
        }

        var (suffix, group) = EmentTable[index];
        var start = w.Length - suffix.Length;
        switch (group)
        {
            case 1:
                if (start >= r2)
                {
                    w = w[..start];
                    if (w.EndsWith("at", StringComparison.Ordinal) && w.Length - 2 >= r2)
                    {
                        w = w[..^2];
                    }
                }

                break;

            case 2:
                if (start >= r2)
                {
                    w = w[..start];
                }
                else if (start >= r1)
                {
                    w = string.Concat(w.AsSpan(0, start), "eux");
                }

                break;

            case 3:
                if (start >= r2)
                {
                    w = w[..start];
                }

                break;

            default:
                if (start >= rv)
                {
                    w = string.Concat(w.AsSpan(0, start), "i");
                }

                break;
        }
    }

    private static void IteFollowUp(ref string w, int r2)
    {
        var index = FindLongestSuffix(w, IteTable, 0);
        if (index < 0)
        {
            return;
        }

        var (suffix, group) = IteTable[index];
        var start = w.Length - suffix.Length;
        switch (group)
        {
            case 1:
                w = start >= r2 ? w[..start] : string.Concat(w.AsSpan(0, start), "abl");
                break;

            case 2:
                w = start >= r2 ? w[..start] : string.Concat(w.AsSpan(0, start), "iqU");
                break;

            default:
                if (start >= r2)
                {
                    w = w[..start];
                }

                break;
        }
    }

    private static bool IVerbSuffix(ref string w, int rv)
    {
        var index = FindLongestSuffix(w, IVerbTable, rv);
        if (index < 0)
        {
            return false;
        }

        var start = w.Length - IVerbTable[index].Suffix.Length;

        // Delete if preceded by a non-vowel (other than H) which is itself in RV.
        if (start < 1 || start - 1 < rv)
        {
            return false;
        }

        var prev = w[start - 1];
        if (prev == 'H' || IsVowel(prev))
        {
            return false;
        }

        w = w[..start];
        return true;
    }

    private static bool VerbSuffix(ref string w, int rv, int r2)
    {
        var index = FindLongestSuffix(w, VerbTable, rv);
        if (index < 0)
        {
            return false;
        }

        var (suffix, group) = VerbTable[index];
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
                w = w[..start];
                return true;

            case 3:
                if (start >= 1 && w[start - 1] == 'e' && start - 1 >= rv)
                {
                    start--;
                }

                w = w[..start];
                return true;

            case 4:
                // ais/aise/aises: not after word-initial X+"al", "auv" or "épl".
                if (IsBlockedAisStem(w, start))
                {
                    return false;
                }

                w = w[..start];
                return true;

            default:
                w = w[..start];
                return true;
        }
    }

    private static bool IsBlockedAisStem(string w, int start)
    {
        // 'al' (next atlimit): exactly one character before "al" (balais, calais, ...).
        if (start == 3 && w[1] == 'a' && w[2] == 'l')
        {
            return true;
        }

        // 'auv' (mauvais) or 'épl' (déplais).
        if (start >= 3)
        {
            var a = w[start - 3];
            var b = w[start - 2];
            var c = w[start - 1];
            if ((a == 'a' && b == 'u' && c == 'v') || (a == 'é' && b == 'p' && c == 'l'))
            {
                return true;
            }
        }

        return false;
    }

    private static void ResidualSuffix(ref string w, int rv, int r2)
    {
        // If the word ends s not preceded by a, i (unless following H), o, u, è or s, delete the s.
        if (w.EndsWith('s'))
        {
            var p = w.Length - 1;
            var keepBlocked = (p >= 2 && w[p - 1] == 'i' && w[p - 2] == 'H')
                || (p >= 1 && w[p - 1] is not ('a' or 'i' or 'o' or 'u' or 'è' or 's'));
            if (keepBlocked)
            {
                w = w[..p];
            }
        }

        var index = FindLongestSuffix(w, ResidualTable, rv);
        if (index < 0)
        {
            return;
        }

        var (suffix, group) = ResidualTable[index];
        var start = w.Length - suffix.Length;
        switch (group)
        {
            case 1:
                if (start >= r2 && start >= 1 && start - 1 >= rv && w[start - 1] is 's' or 't')
                {
                    w = w[..start];
                }

                break;

            case 2:
                w = string.Concat(w.AsSpan(0, start), "i");
                break;

            default:
                w = w[..start];
                break;
        }
    }

    private static void UnDouble(ref string w)
    {
        if (w.EndsWith("enn", StringComparison.Ordinal)
            || w.EndsWith("onn", StringComparison.Ordinal)
            || w.EndsWith("ett", StringComparison.Ordinal)
            || w.EndsWith("ell", StringComparison.Ordinal)
            || w.EndsWith("eill", StringComparison.Ordinal))
        {
            w = w[..^1];
        }
    }

    private static void UnAccent(ref string w)
    {
        var j = w.Length - 1;
        while (j >= 0 && !IsVowel(w[j]))
        {
            j--;
        }

        if (j >= 0 && j < w.Length - 1 && (w[j] == 'é' || w[j] == 'è'))
        {
            w = string.Concat(w.AsSpan(0, j), "e", w.AsSpan(j + 1));
        }
    }

    private static string Postlude(string w)
    {
        var sb = new StringBuilder(w.Length);
        for (var i = 0; i < w.Length; i++)
        {
            switch (w[i])
            {
                case 'I':
                    sb.Append('i');
                    break;

                case 'U':
                    sb.Append('u');
                    break;

                case 'Y':
                    sb.Append('y');
                    break;

                case 'H':
                    if (i + 1 < w.Length && w[i + 1] == 'e')
                    {
                        sb.Append('ë');
                        i++;
                    }
                    else if (i + 1 < w.Length && w[i + 1] == 'i')
                    {
                        sb.Append('ï');
                        i++;
                    }

                    break;

                default:
                    sb.Append(w[i]);
                    break;
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
