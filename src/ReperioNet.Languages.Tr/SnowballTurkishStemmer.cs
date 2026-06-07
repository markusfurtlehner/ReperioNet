using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Tr;

/// <summary>
/// Snowball stemmer for Turkish; a faithful port of the official algorithm published at
/// <see href="https://snowballstem.org/algorithms/turkish/stemmer.html"/> (Evren Kapusuz Çilden's
/// affix-stripping stemmer). Strips nominal verb suffixes and noun suffixes with vowel-harmony
/// and buffer-consonant (y/n/s/U) checks, handles the recursive <c>-ki</c> suffix chains, and
/// post-processes the last consonant (b→p, c→ç, d→t, ğ→k) with the d/g vowel-restoring rule.
/// </summary>
/// <remarks>
/// The implementation keeps all working state in locals, so a single instance is safe for
/// concurrent use.
/// </remarks>
public sealed class SnowballTurkishStemmer : IStemmer
{
    private const string Vowels = "aeıioöuü";
    private const string UVowels = "ıiuü";

    // Suffix tables, longest entries first (find_among_b longest-match order).
    private static readonly string[] PossessiveSuffixes =
    [
        "mız", "miz", "muz", "müz", "nız", "niz", "nuz", "nüz", "m", "n",
    ];

    private static readonly string[] LArISuffixes = ["leri", "ları"];
    private static readonly string[] NUSuffixes = ["nı", "ni", "nu", "nü"];
    private static readonly string[] NUnSuffixes = ["ın", "in", "un", "ün"];
    private static readonly string[] YASuffixes = ["a", "e"];
    private static readonly string[] NASuffixes = ["na", "ne"];
    private static readonly string[] DASuffixes = ["da", "de", "ta", "te"];
    private static readonly string[] NdASuffixes = ["nda", "nde"];
    private static readonly string[] DAnSuffixes = ["dan", "den", "tan", "ten"];
    private static readonly string[] NdAnSuffixes = ["ndan", "nden"];
    private static readonly string[] YlASuffixes = ["la", "le"];
    private static readonly string[] NcASuffixes = ["ca", "ce"];
    private static readonly string[] YUmSuffixes = ["ım", "im", "um", "üm"];
    private static readonly string[] SUnSuffixes = ["sın", "sin", "sun", "sün"];
    private static readonly string[] YUzSuffixes = ["ız", "iz", "uz", "üz"];
    private static readonly string[] SUnUzSuffixes = ["sınız", "siniz", "sunuz", "sünüz"];
    private static readonly string[] LArSuffixes = ["ler", "lar"];
    private static readonly string[] NUzSuffixes = ["nız", "niz", "nuz", "nüz"];
    private static readonly string[] DUrSuffixes = ["tır", "tir", "tur", "tür", "dır", "dir", "dur", "dür"];
    private static readonly string[] CAsInASuffixes = ["casına", "cesine"];

    private static readonly string[] YDUSuffixes =
    [
        "tım", "tim", "tum", "tüm", "dım", "dim", "dum", "düm",
        "tın", "tin", "tun", "tün", "dın", "din", "dun", "dün",
        "tık", "tik", "tuk", "tük", "dık", "dik", "duk", "dük",
        "tı", "ti", "tu", "tü", "dı", "di", "du", "dü",
    ];

    private static readonly string[] YsASuffixes = ["sam", "san", "sak", "sem", "sen", "sek", "sa", "se"];
    private static readonly string[] YmUsSuffixes = ["mış", "miş", "muş", "müş"];

    /// <inheritdoc />
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return token;
        }

        var s = RemoveProperNounSuffix(token);
        if (!MoreThanOneSyllable(s))
        {
            return s;
        }

        var continueNounSuffixes = true;
        s = StemNominalVerbSuffixes(s, ref continueNounSuffixes);
        if (!continueNounSuffixes)
        {
            // The -lAr branch ends stemming early: post-processing is skipped too.
            return s;
        }

        s = StemNounSuffixes(s);
        return Postlude(s);
    }

    private static bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;

    private static string RemoveProperNounSuffix(string s)
    {
        // Strip leading apostrophes left by tokenization of quoted text.
        var i = 0;
        while (i < s.Length && s[i] == '\'')
        {
            i++;
        }

        if (i > 0)
        {
            s = s[i..];
        }

        // Truncate at an apostrophe separating a proper name from its suffixes, provided at
        // least two characters precede it.
        if (s.Length >= 2)
        {
            var idx = s.IndexOf('\'', 2);
            if (idx >= 0)
            {
                s = s[..idx];
            }
        }

        return s;
    }

    private static bool MoreThanOneSyllable(string s)
    {
        var vowels = 0;
        foreach (var c in s)
        {
            if (IsVowel(c) && ++vowels == 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Vowel-harmony test at cursor <paramref name="c"/>: the last vowel before the cursor must be
    /// preceded (anywhere to its left) by a vowel of the matching harmony class.
    /// </summary>
    private static bool CheckVowelHarmony(string s, int c)
    {
        var j = c - 1;
        while (j >= 0 && !IsVowel(s[j]))
        {
            j--;
        }

        if (j < 0)
        {
            return false;
        }

        var group = s[j] switch
        {
            'a' => "aıou",
            'e' => "eiöü",
            'ı' => "aı",
            'i' => "ei",
            'o' or 'u' => "ou",
            _ => "öü", // 'ö' or 'ü'
        };

        for (var k = j - 1; k >= 0; k--)
        {
            if (group.IndexOf(s[k]) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Optional buffer-consonant check (y/n/s): consumes the consonant when it follows a vowel;
    /// otherwise requires the character before the cursor to be followed by a vowel.
    /// </summary>
    private static bool OptionalConsonant(string s, ref int c, char consonant)
    {
        if (c >= 1 && s[c - 1] == consonant)
        {
            if (c >= 2 && IsVowel(s[c - 2]))
            {
                c--;
                return true;
            }

            return false;
        }

        return c >= 2 && IsVowel(s[c - 2]);
    }

    /// <summary>Optional buffer-vowel check (U = ı/i/u/ü): consumes the vowel when it follows a non-vowel.</summary>
    private static bool OptionalUVowel(string s, ref int c)
    {
        if (c >= 1 && UVowels.IndexOf(s[c - 1]) >= 0)
        {
            if (c >= 2 && !IsVowel(s[c - 2]))
            {
                c--;
                return true;
            }

            return false;
        }

        return c >= 2 && !IsVowel(s[c - 2]);
    }

    /// <summary>Longest match among <paramref name="suffixes"/> ending at <paramref name="c"/>; the new cursor, or -1.</summary>
    private static int MatchAny(string s, int c, string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (c >= suffix.Length && string.CompareOrdinal(s, c - suffix.Length, suffix, 0, suffix.Length) == 0)
            {
                return c - suffix.Length;
            }
        }

        return -1;
    }

    private static int MarkPossessives(string s, int c)
    {
        var nc = MatchAny(s, c, PossessiveSuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalUVowel(s, ref nc) ? nc : -1;
    }

    private static int MarkSU(string s, int c)
    {
        if (!CheckVowelHarmony(s, c) || c < 1 || UVowels.IndexOf(s[c - 1]) < 0)
        {
            return -1;
        }

        var nc = c - 1;
        return OptionalConsonant(s, ref nc, 's') ? nc : -1;
    }

    private static int MarkLArI(string s, int c) => MatchAny(s, c, LArISuffixes);

    private static int MarkYU(string s, int c)
    {
        if (!CheckVowelHarmony(s, c) || c < 1 || UVowels.IndexOf(s[c - 1]) < 0)
        {
            return -1;
        }

        var nc = c - 1;
        return OptionalConsonant(s, ref nc, 'y') ? nc : -1;
    }

    private static int MarkNU(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, NUSuffixes) : -1;

    private static int MarkNUn(string s, int c)
    {
        if (!CheckVowelHarmony(s, c))
        {
            return -1;
        }

        var nc = MatchAny(s, c, NUnSuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalConsonant(s, ref nc, 'n') ? nc : -1;
    }

    private static int MarkYA(string s, int c)
    {
        if (!CheckVowelHarmony(s, c))
        {
            return -1;
        }

        var nc = MatchAny(s, c, YASuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalConsonant(s, ref nc, 'y') ? nc : -1;
    }

    private static int MarkNA(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, NASuffixes) : -1;

    private static int MarkDA(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, DASuffixes) : -1;

    private static int MarkNdA(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, NdASuffixes) : -1;

    private static int MarkDAn(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, DAnSuffixes) : -1;

    private static int MarkNdAn(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, NdAnSuffixes) : -1;

    private static int MarkYlA(string s, int c)
    {
        if (!CheckVowelHarmony(s, c))
        {
            return -1;
        }

        var nc = MatchAny(s, c, YlASuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalConsonant(s, ref nc, 'y') ? nc : -1;
    }

    private static int MarkKi(string s, int c)
        => c >= 2 && s[c - 2] == 'k' && s[c - 1] == 'i' ? c - 2 : -1;

    private static int MarkNcA(string s, int c)
    {
        if (!CheckVowelHarmony(s, c))
        {
            return -1;
        }

        var nc = MatchAny(s, c, NcASuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalConsonant(s, ref nc, 'n') ? nc : -1;
    }

    private static int MarkYUm(string s, int c)
    {
        if (!CheckVowelHarmony(s, c))
        {
            return -1;
        }

        var nc = MatchAny(s, c, YUmSuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalConsonant(s, ref nc, 'y') ? nc : -1;
    }

    private static int MarkSUn(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, SUnSuffixes) : -1;

    private static int MarkYUz(string s, int c)
    {
        if (!CheckVowelHarmony(s, c))
        {
            return -1;
        }

        var nc = MatchAny(s, c, YUzSuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalConsonant(s, ref nc, 'y') ? nc : -1;
    }

    private static int MarkSUnUz(string s, int c) => MatchAny(s, c, SUnUzSuffixes);

    private static int MarkLAr(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, LArSuffixes) : -1;

    private static int MarkNUz(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, NUzSuffixes) : -1;

    private static int MarkDUr(string s, int c)
        => CheckVowelHarmony(s, c) ? MatchAny(s, c, DUrSuffixes) : -1;

    private static int MarkCAsInA(string s, int c) => MatchAny(s, c, CAsInASuffixes);

    private static int MarkYDU(string s, int c)
    {
        if (!CheckVowelHarmony(s, c))
        {
            return -1;
        }

        var nc = MatchAny(s, c, YDUSuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalConsonant(s, ref nc, 'y') ? nc : -1;
    }

    private static int MarkYsA(string s, int c)
    {
        var nc = MatchAny(s, c, YsASuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalConsonant(s, ref nc, 'y') ? nc : -1;
    }

    private static int MarkYmUs(string s, int c)
    {
        if (!CheckVowelHarmony(s, c))
        {
            return -1;
        }

        var nc = MatchAny(s, c, YmUsSuffixes);
        if (nc < 0)
        {
            return -1;
        }

        return OptionalConsonant(s, ref nc, 'y') ? nc : -1;
    }

    private static int MarkYken(string s, int c)
    {
        if (c < 3 || string.CompareOrdinal(s, c - 3, "ken", 0, 3) != 0)
        {
            return -1;
        }

        var nc = c - 3;
        return OptionalConsonant(s, ref nc, 'y') ? nc : -1;
    }

    private static string StemNominalVerbSuffixes(string s, ref bool continueNounSuffixes)
    {
        var end = s.Length;
        continueNounSuffixes = true;

        // Branch 1: -(y)mUş / -(y)DU / -(y)sA / -(y)ken.
        var c = MarkYmUs(s, end);
        if (c < 0)
        {
            c = MarkYDU(s, end);
        }

        if (c < 0)
        {
            c = MarkYsA(s, end);
        }

        if (c < 0)
        {
            c = MarkYken(s, end);
        }

        if (c >= 0)
        {
            return s[..c];
        }

        // Branch 2: -cAsInA preceded by an optional person suffix and a mandatory -(y)mUş.
        c = MarkCAsInA(s, end);
        if (c >= 0)
        {
            var c2 = MarkSUnUz(s, c);
            if (c2 < 0)
            {
                c2 = MarkLAr(s, c);
            }

            if (c2 < 0)
            {
                c2 = MarkYUm(s, c);
            }

            if (c2 < 0)
            {
                c2 = MarkSUn(s, c);
            }

            if (c2 < 0)
            {
                c2 = MarkYUz(s, c);
            }

            if (c2 < 0)
            {
                c2 = c;
            }

            var c3 = MarkYmUs(s, c2);
            if (c3 >= 0)
            {
                return s[..c3];
            }

            // The branch fails without modification; fall through to the next branch.
        }

        // Branch 3: -lAr, optionally followed (in the word) by -DUr/-(y)DU/-(y)sA/-(y)mUş;
        // stops further noun-suffix stemming.
        c = MarkLAr(s, end);
        if (c >= 0)
        {
            s = s[..c];
            var c2 = MarkDUr(s, c);
            if (c2 < 0)
            {
                c2 = MarkYDU(s, c);
            }

            if (c2 < 0)
            {
                c2 = MarkYsA(s, c);
            }

            if (c2 < 0)
            {
                c2 = MarkYmUs(s, c);
            }

            if (c2 >= 0)
            {
                s = s[..c2];
            }

            continueNounSuffixes = false;
            return s;
        }

        // Branch 4: -nUz followed by -(y)DU or -(y)sA.
        c = MarkNUz(s, end);
        if (c >= 0)
        {
            var c2 = MarkYDU(s, c);
            if (c2 < 0)
            {
                c2 = MarkYsA(s, c);
            }

            if (c2 >= 0)
            {
                return s[..c2];
            }
        }

        // Branch 5: -sUnUz / -(y)Uz / -sUn / -(y)Um, with an optional preceding -(y)mUş.
        c = MarkSUnUz(s, end);
        if (c < 0)
        {
            c = MarkYUz(s, end);
        }

        if (c < 0)
        {
            c = MarkSUn(s, end);
        }

        if (c < 0)
        {
            c = MarkYUm(s, end);
        }

        if (c >= 0)
        {
            s = s[..c];
            var c2 = MarkYmUs(s, c);
            if (c2 >= 0)
            {
                s = s[..c2];
            }

            return s;
        }

        // Branch 6: -DUr, optionally preceded by a person suffix and a mandatory -(y)mUş.
        c = MarkDUr(s, end);
        if (c >= 0)
        {
            s = s[..c];
            var c2 = MarkSUnUz(s, c);
            if (c2 < 0)
            {
                c2 = MarkLAr(s, c);
            }

            if (c2 < 0)
            {
                c2 = MarkYUm(s, c);
            }

            if (c2 < 0)
            {
                c2 = MarkSUn(s, c);
            }

            if (c2 < 0)
            {
                c2 = MarkYUz(s, c);
            }

            if (c2 < 0)
            {
                c2 = c;
            }

            var c3 = MarkYmUs(s, c2);
            if (c3 >= 0)
            {
                s = s[..c3];
            }

            return s;
        }

        return s;
    }

    /// <summary>
    /// Stems a noun-suffix chain ending in <c>-ki</c> (e.g. <c>-daki</c>, <c>-nınki</c>,
    /// <c>-ndaki</c>), recursing for nested chains. Mutates <paramref name="s"/> only on success.
    /// </summary>
    private static bool StemSuffixChainBeforeKi(ref string s, int c)
    {
        var ket = c;
        var ki = MarkKi(s, c);
        if (ki < 0)
        {
            return false;
        }

        // -DA + ki
        var p = MarkDA(s, ki);
        if (p >= 0)
        {
            s = s.Remove(p, ket - p);
            var ket2 = p;
            var q = MarkLAr(s, p);
            if (q >= 0)
            {
                s = s.Remove(q, ket2 - q);
                StemSuffixChainBeforeKi(ref s, q);
                return true;
            }

            q = MarkPossessives(s, p);
            if (q >= 0)
            {
                s = s.Remove(q, ket2 - q);
                var ket3 = q;
                var r = MarkLAr(s, q);
                if (r >= 0)
                {
                    s = s.Remove(r, ket3 - r);
                    StemSuffixChainBeforeKi(ref s, r);
                }
            }

            return true;
        }

        // -nUn + ki
        p = MarkNUn(s, ki);
        if (p >= 0)
        {
            s = s.Remove(p, ket - p);
            var ket2 = p;
            var q = MarkLArI(s, p);
            if (q >= 0)
            {
                s = s.Remove(q, ket2 - q);
                return true;
            }

            q = MarkPossessives(s, p);
            if (q < 0)
            {
                q = MarkSU(s, p);
            }

            if (q >= 0)
            {
                s = s.Remove(q, ket2 - q);
                var ket3 = q;
                var r = MarkLAr(s, q);
                if (r >= 0)
                {
                    s = s.Remove(r, ket3 - r);
                    StemSuffixChainBeforeKi(ref s, r);
                }

                return true;
            }

            StemSuffixChainBeforeKi(ref s, p);
            return true;
        }

        // -ndA + ki (the -ndA itself is only removed together with a deeper suffix).
        p = MarkNdA(s, ki);
        if (p >= 0)
        {
            var q = MarkLArI(s, p);
            if (q >= 0)
            {
                s = s.Remove(q, ket - q);
                return true;
            }

            q = MarkSU(s, p);
            if (q >= 0)
            {
                s = s.Remove(q, ket - q);
                var ket2 = q;
                var r = MarkLAr(s, q);
                if (r >= 0)
                {
                    s = s.Remove(r, ket2 - r);
                    StemSuffixChainBeforeKi(ref s, r);
                }

                return true;
            }

            return StemSuffixChainBeforeKi(ref s, p);
        }

        return false;
    }

    private static string StemNounSuffixes(string s)
    {
        // Branch 1: -lAr.
        var c = MarkLAr(s, s.Length);
        if (c >= 0)
        {
            s = s[..c];
            StemSuffixChainBeforeKi(ref s, c);
            return s;
        }

        // Branch 2: -ncA.
        c = MarkNcA(s, s.Length);
        if (c >= 0)
        {
            s = s[..c];
            var q = MarkLArI(s, c);
            if (q >= 0)
            {
                return s[..q];
            }

            q = MarkPossessives(s, c);
            if (q < 0)
            {
                q = MarkSU(s, c);
            }

            if (q >= 0)
            {
                s = s[..q];
                var r = MarkLAr(s, q);
                if (r >= 0)
                {
                    s = s[..r];
                    StemSuffixChainBeforeKi(ref s, r);
                }

                return s;
            }

            q = MarkLAr(s, c);
            if (q >= 0)
            {
                s = s[..q];
                StemSuffixChainBeforeKi(ref s, q);
            }

            return s;
        }

        // Branch 3: -ndA / -nA followed by a deeper suffix or a -ki chain.
        c = MarkNdA(s, s.Length);
        if (c < 0)
        {
            c = MarkNA(s, s.Length);
        }

        if (c >= 0)
        {
            var q = MarkLArI(s, c);
            if (q >= 0)
            {
                return s[..q];
            }

            q = MarkSU(s, c);
            if (q >= 0)
            {
                s = s[..q];
                var r = MarkLAr(s, q);
                if (r >= 0)
                {
                    s = s[..r];
                    StemSuffixChainBeforeKi(ref s, r);
                }

                return s;
            }

            if (StemSuffixChainBeforeKi(ref s, c))
            {
                return s;
            }

            // All alternatives failed: the branch fails without modification.
        }

        // Branch 4: -ndAn / -nU followed by -sU (removed) or -lArI (matched only).
        c = MarkNdAn(s, s.Length);
        if (c < 0)
        {
            c = MarkNU(s, s.Length);
        }

        if (c >= 0)
        {
            var q = MarkSU(s, c);
            if (q >= 0)
            {
                s = s[..q];
                var r = MarkLAr(s, q);
                if (r >= 0)
                {
                    s = s[..r];
                    StemSuffixChainBeforeKi(ref s, r);
                }

                return s;
            }

            if (MarkLArI(s, c) >= 0)
            {
                return s;
            }
        }

        // Branch 5: -DAn.
        c = MarkDAn(s, s.Length);
        if (c >= 0)
        {
            s = s[..c];
            var q = MarkPossessives(s, c);
            if (q >= 0)
            {
                s = s[..q];
                var r = MarkLAr(s, q);
                if (r >= 0)
                {
                    s = s[..r];
                    StemSuffixChainBeforeKi(ref s, r);
                }

                return s;
            }

            q = MarkLAr(s, c);
            if (q >= 0)
            {
                s = s[..q];
                StemSuffixChainBeforeKi(ref s, q);
                return s;
            }

            StemSuffixChainBeforeKi(ref s, c);
            return s;
        }

        // Branch 6: -nUn / -(y)lA.
        c = MarkNUn(s, s.Length);
        if (c < 0)
        {
            c = MarkYlA(s, s.Length);
        }

        if (c >= 0)
        {
            s = s[..c];

            // First alternative: -lAr plus a mandatory -ki chain. When the chain fails the
            // -lAr removal still stands and the remaining alternatives run at the new end.
            var q = MarkLAr(s, c);
            if (q >= 0)
            {
                s = s[..q];
                if (StemSuffixChainBeforeKi(ref s, q))
                {
                    return s;
                }
            }

            var end = s.Length;
            var q2 = MarkPossessives(s, end);
            if (q2 < 0)
            {
                q2 = MarkSU(s, end);
            }

            if (q2 >= 0)
            {
                s = s[..q2];
                var r = MarkLAr(s, q2);
                if (r >= 0)
                {
                    s = s[..r];
                    StemSuffixChainBeforeKi(ref s, r);
                }

                return s;
            }

            StemSuffixChainBeforeKi(ref s, end);
            return s;
        }

        // Branch 7: -lArI.
        c = MarkLArI(s, s.Length);
        if (c >= 0)
        {
            return s[..c];
        }

        // Branch 8: a bare -ki chain.
        if (StemSuffixChainBeforeKi(ref s, s.Length))
        {
            return s;
        }

        // Branch 9: -DA / -(y)U / -(y)A.
        c = MarkDA(s, s.Length);
        if (c < 0)
        {
            c = MarkYU(s, s.Length);
        }

        if (c < 0)
        {
            c = MarkYA(s, s.Length);
        }

        if (c >= 0)
        {
            s = s[..c];
            var q = MarkPossessives(s, c);
            if (q >= 0)
            {
                s = s[..q];
                var r = MarkLAr(s, q);
                if (r >= 0)
                {
                    s = s[..r];
                }

                StemSuffixChainBeforeKi(ref s, s.Length);
                return s;
            }

            q = MarkLAr(s, c);
            if (q >= 0)
            {
                s = s[..q];
                StemSuffixChainBeforeKi(ref s, q);
            }

            return s;
        }

        // Branch 10: a possessive or -sU.
        c = MarkPossessives(s, s.Length);
        if (c < 0)
        {
            c = MarkSU(s, s.Length);
        }

        if (c >= 0)
        {
            s = s[..c];
            var q = MarkLAr(s, c);
            if (q >= 0)
            {
                s = s[..q];
                StemSuffixChainBeforeKi(ref s, q);
            }
        }

        return s;
    }

    private static string Postlude(string s)
    {
        if (s == "ad" || s == "soyad")
        {
            // Reserved words are returned untouched.
            return s;
        }

        s = AppendUToStemsEndingWithDOrG(s);
        return PostProcessLastConsonant(s);
    }

    private static string AppendUToStemsEndingWithDOrG(string s)
    {
        if (s.Length == 0 || (s[^1] != 'd' && s[^1] != 'g'))
        {
            return s;
        }

        for (var j = s.Length - 2; j >= 0; j--)
        {
            switch (s[j])
            {
                case 'a' or 'ı':
                    return s + "ı";
                case 'e' or 'i':
                    return s + "i";
                case 'o' or 'u':
                    return s + "u";
                case 'ö' or 'ü':
                    return s + "ü";
                default:
                    continue;
            }
        }

        return s;
    }

    private static string PostProcessLastConsonant(string s)
    {
        if (s.Length == 0)
        {
            return s;
        }

        var replacement = s[^1] switch
        {
            'b' => 'p',
            'c' => 'ç',
            'd' => 't',
            'ğ' => 'k',
            _ => '\0',
        };

        return replacement == '\0' ? s : s[..^1] + replacement;
    }
}
