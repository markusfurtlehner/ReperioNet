// Faithful port of the official Snowball Romanian stemming algorithm:
// https://snowballstem.org/algorithms/romanian/stemmer.html
// (Snowball 3.x romanian.sbl: cedilla -> comma-below normalization, U/I marking, RV/R1/R2,
// step 0 plural/article removal, combining suffixes, standard suffixes, verb suffixes,
// final vowel removal.)

using System.Text;
using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Ro;

/// <summary>
/// Romanian Snowball stemmer. A pure managed port of the official Snowball Romanian algorithm
/// (see <c>https://snowballstem.org/algorithms/romanian/stemmer.html</c>). Accepts both the
/// comma-below (ș/ț) and legacy cedilla (ş/ţ) spellings; cedilla forms are normalized first.
/// Stateless and thread-safe: all working state lives in locals.
/// </summary>
public sealed class SnowballRomanianStemmer : IStemmer
{
    private const char SComma = 'ș';   // ș
    private const char TComma = 'ț';   // ț
    private const char SCedilla = 'ş'; // ş
    private const char TCedilla = 'ţ'; // ţ

    // Step 0: plural/article endings (R1).
    private static readonly (string Suffix, int Group)[] Step0Table = ByLengthDescending(new[]
    {
        ("ul", 1), ("ului", 1),
        ("aua", 2),
        ("ea", 3), ("ele", 3), ("elor", 3),
        ("ii", 4), ("iua", 4), ("iei", 4), ("iile", 4), ("iilor", 4), ("ilor", 4),
        ("ile", 5),
        ("atei", 6),
        ("ație", 7), ("ația", 7),
    });

    // Step 1: combining suffixes (R1), repeated until none matches.
    private static readonly (string Suffix, int Group)[] ComboTable = ByLengthDescending(new[]
    {
        ("abilitate", 1), ("abilitati", 1), ("abilităi", 1), ("abilități", 1),
        ("ibilitate", 2),
        ("ivitate", 3), ("ivitati", 3), ("ivităi", 3), ("ivități", 3),
        ("icitate", 4), ("icitati", 4), ("icităi", 4), ("icități", 4),
        ("icator", 4), ("icatori", 4),
        ("iciv", 4), ("iciva", 4), ("icive", 4), ("icivi", 4), ("icivă", 4),
        ("ical", 4), ("icala", 4), ("icale", 4), ("icali", 4), ("icală", 4),
        ("ativ", 5), ("ativa", 5), ("ative", 5), ("ativi", 5), ("ativă", 5),
        ("ațiune", 5), ("atoare", 5), ("ator", 5), ("atori", 5),
        ("ătoare", 5), ("ător", 5), ("ători", 5),
        ("itiv", 6), ("itiva", 6), ("itive", 6), ("itivi", 6), ("itivă", 6),
        ("ițiune", 6), ("itoare", 6), ("itor", 6), ("itori", 6),
    });

    // Step 2: standard suffixes (R2).
    private static readonly (string Suffix, int Group)[] Step2Table = ByLengthDescending(new[]
    {
        ("at", 1), ("ata", 1), ("ată", 1), ("ati", 1), ("ate", 1),
        ("ut", 1), ("uta", 1), ("ută", 1), ("uti", 1), ("ute", 1),
        ("it", 1), ("ita", 1), ("ită", 1), ("iti", 1), ("ite", 1),
        ("ic", 1), ("ica", 1), ("ice", 1), ("ici", 1), ("ică", 1),
        ("abil", 1), ("abila", 1), ("abile", 1), ("abili", 1), ("abilă", 1),
        ("ibil", 1), ("ibila", 1), ("ibile", 1), ("ibili", 1), ("ibilă", 1),
        ("oasa", 1), ("oasă", 1), ("oase", 1), ("os", 1), ("osi", 1), ("oși", 1),
        ("ant", 1), ("anta", 1), ("ante", 1), ("anti", 1), ("antă", 1),
        ("ator", 1), ("atori", 1),
        ("itate", 1), ("itati", 1), ("ităi", 1), ("ități", 1),
        ("iv", 1), ("iva", 1), ("ive", 1), ("ivi", 1), ("ivă", 1),
        ("iune", 2), ("iuni", 2),
        ("ism", 3), ("isme", 3),
        ("ist", 3), ("ista", 3), ("iste", 3), ("isti", 3), ("istă", 3), ("iști", 3),
    });

    // Step 3: verb suffixes; the whole step is confined to RV.
    // Group 1: delete when preceded (in RV) by a non-vowel or u; group 2: delete.
    private static readonly (string Suffix, int Group)[] VerbTable = ByLengthDescending(new[]
    {
        ("are", 1), ("ere", 1), ("ire", 1), ("âre", 1),
        ("ind", 1), ("ând", 1), ("indu", 1), ("ându", 1),
        ("eze", 1), ("ească", 1),
        ("ez", 1), ("ezi", 1), ("ează", 1), ("esc", 1), ("ești", 1), ("ește", 1),
        ("ăsc", 1), ("ăști", 1), ("ăște", 1),
        ("am", 1), ("ai", 1), ("au", 1),
        ("eam", 1), ("eai", 1), ("ea", 1), ("eați", 1), ("eau", 1),
        ("iam", 1), ("iai", 1), ("ia", 1), ("iați", 1), ("iau", 1),
        ("ui", 1),
        ("ași", 1), ("arăm", 1), ("arăți", 1), ("ară", 1),
        ("uși", 1), ("urăm", 1), ("urăți", 1), ("ură", 1),
        ("iși", 1), ("irăm", 1), ("irăți", 1), ("iră", 1),
        ("âi", 1), ("âși", 1), ("ârăm", 1), ("ârăți", 1),
        ("âră", 1),
        ("asem", 1), ("aseși", 1), ("ase", 1), ("aserăm", 1), ("aserăți", 1),
        ("aseră", 1),
        ("isem", 1), ("iseși", 1), ("ise", 1), ("iserăm", 1), ("iserăți", 1),
        ("iseră", 1),
        ("âsem", 1), ("âseși", 1), ("âse", 1), ("âserăm", 1),
        ("âserăți", 1), ("âseră", 1),
        ("usem", 1), ("useși", 1), ("use", 1), ("userăm", 1), ("userăți", 1),
        ("useră", 1),
        ("ăm", 2), ("ați", 2), ("em", 2), ("eți", 2), ("im", 2), ("iți", 2),
        ("âm", 2), ("âți", 2),
        ("seși", 2), ("serăm", 2), ("serăți", 2), ("seră", 2),
        ("sei", 2), ("se", 2),
        ("sesem", 2), ("seseși", 2), ("sese", 2), ("seserăm", 2), ("seserăți", 2),
        ("seseră", 2),
    });

    // Step 4: final vowel.
    private static readonly (string Suffix, int Group)[] VowelTable = ByLengthDescending(new[]
    {
        ("ie", 0), ("a", 0), ("e", 0), ("i", 0), ("ă", 0),
    });

    /// <inheritdoc />
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        var word = Normalize(token);
        word = Prelude(word);
        MarkRegions(word, out var rv, out var r1, out var r2);

        Step0(ref word, r1);
        var standardSuffixRemoved = StandardSuffix(ref word, r1, r2);
        if (!standardSuffixRemoved)
        {
            VerbSuffix(ref word, rv);
        }

        VowelSuffix(ref word, rv);
        return Postlude(word);
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u'
        or 'â' or 'î' or 'ă'; // â î ă

    private static string Normalize(string w)
    {
        if (w.IndexOf(SCedilla) < 0 && w.IndexOf(TCedilla) < 0)
        {
            return w;
        }

        var sb = new StringBuilder(w.Length);
        foreach (var c in w)
        {
            sb.Append(c switch
            {
                SCedilla => SComma,
                TCedilla => TComma,
                _ => c,
            });
        }

        return sb.ToString();
    }

    private static string Prelude(string w)
    {
        // Mark u/i between vowels as U/I.
        var sb = new StringBuilder(w);
        var i = 0;
        while (i < sb.Length)
        {
            if (IsVowel(sb[i]) && i + 2 < sb.Length && (sb[i + 1] == 'u' || sb[i + 1] == 'i')
                && IsVowel(sb[i + 2]))
            {
                // The trailing vowel can anchor the next match (Snowball goto re-scans).
                sb[i + 1] = sb[i + 1] == 'u' ? 'U' : 'I';
                i += 2;
            }
            else
            {
                i++;
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

    private static void Step0(ref string w, int r1)
    {
        var index = FindLongestSuffix(w, Step0Table, 0);
        if (index < 0)
        {
            return;
        }

        var (suffix, group) = Step0Table[index];
        var start = w.Length - suffix.Length;
        if (start < r1)
        {
            return;
        }

        switch (group)
        {
            case 1:
                w = w[..start];
                break;

            case 2:
                w = string.Concat(w.AsSpan(0, start), "a");
                break;

            case 3:
                w = string.Concat(w.AsSpan(0, start), "e");
                break;

            case 4:
                w = string.Concat(w.AsSpan(0, start), "i");
                break;

            case 5:
                // ile -> i, unless preceded by ab.
                if (start >= 2 && w[start - 2] == 'a' && w[start - 1] == 'b')
                {
                    return;
                }

                w = string.Concat(w.AsSpan(0, start), "i");
                break;

            case 6:
                w = string.Concat(w.AsSpan(0, start), "at");
                break;

            default:
                w = string.Concat(w.AsSpan(0, start), "ați");
                break;
        }
    }

    private static bool StandardSuffix(ref string w, int r1, int r2)
    {
        var removed = false;

        // Step 1: repeat the combining suffix replacements.
        while (true)
        {
            var comboIndex = FindLongestSuffix(w, ComboTable, 0);
            if (comboIndex < 0)
            {
                break;
            }

            var (comboSuffix, comboGroup) = ComboTable[comboIndex];
            var comboStart = w.Length - comboSuffix.Length;
            if (comboStart < r1)
            {
                break;
            }

            w = string.Concat(w.AsSpan(0, comboStart), comboGroup switch
            {
                1 => "abil",
                2 => "ibil",
                3 => "iv",
                4 => "ic",
                5 => "at",
                _ => "it",
            });
            removed = true;
        }

        // Step 2: standard suffixes in R2.
        var index = FindLongestSuffix(w, Step2Table, 0);
        if (index < 0)
        {
            return removed;
        }

        var (suffix, group) = Step2Table[index];
        var start = w.Length - suffix.Length;
        if (start < r2)
        {
            return removed;
        }

        switch (group)
        {
            case 1:
                w = w[..start];
                return true;

            case 2:
                // iune/iuni: only after ț, which becomes t.
                if (start < 1 || w[start - 1] != TComma)
                {
                    return removed;
                }

                w = string.Concat(w.AsSpan(0, start - 1), "t");
                return true;

            default:
                w = string.Concat(w.AsSpan(0, start), "ist");
                return true;
        }
    }

    private static void VerbSuffix(ref string w, int rv)
    {
        var index = FindLongestSuffix(w, VerbTable, rv);
        if (index < 0)
        {
            return;
        }

        var (suffix, group) = VerbTable[index];
        var start = w.Length - suffix.Length;
        if (group == 1)
        {
            // Delete only when preceded, within RV, by a non-vowel or by u.
            if (start < 1 || start - 1 < rv)
            {
                return;
            }

            var prev = w[start - 1];
            if (IsVowel(prev) && prev != 'u')
            {
                return;
            }
        }

        w = w[..start];
    }

    private static void VowelSuffix(ref string w, int rv)
    {
        var index = FindLongestSuffix(w, VowelTable, 0);
        if (index < 0)
        {
            return;
        }

        var start = w.Length - VowelTable[index].Suffix.Length;
        if (start >= rv)
        {
            w = w[..start];
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

    private static (string Suffix, int Group)[] ByLengthDescending((string, int)[] rules)
        => rules.OrderByDescending(static r => r.Item1.Length).ToArray();
}
