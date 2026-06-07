using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Hu;

/// <summary>
/// Snowball stemmer for Hungarian; a faithful port of the official algorithm published at
/// <see href="https://snowballstem.org/algorithms/hungarian/stemmer.html"/>. Removes noun
/// inflections (instrumental, case endings, factive, owned/owner and plural forms), normalizing
/// a trailing accented <c>á</c>/<c>é</c> back to <c>a</c>/<c>e</c> where the algorithm specifies.
/// </summary>
/// <remarks>
/// The implementation keeps all working state in locals, so a single instance is safe for
/// concurrent use.
/// </remarks>
public sealed class SnowballHungarianStemmer : IStemmer
{
    private const string Vowels = "aeiouáéíóöőúüű";

    /// <summary>The consonants that form simple doubled pairs (bb, cc, dd, ...).</summary>
    private const string SimpleDoubleConsonants = "bcdfgjklmnprstvz";

    private enum SuffixAction
    {
        Delete,
        ReplaceWithA,
        ReplaceWithE,
    }

    // All tables are ordered longest suffix first: the algorithm commits to the longest match
    // before testing R1, and fails the whole step if that match lies outside R1.
    private static readonly (string Suffix, SuffixAction Action)[] CaseSuffixes =
    [
        ("képpen", SuffixAction.Delete), ("onként", SuffixAction.Delete),
        ("enként", SuffixAction.Delete), ("anként", SuffixAction.Delete),
        ("képp", SuffixAction.Delete), ("ként", SuffixAction.Delete),
        ("ban", SuffixAction.Delete), ("ben", SuffixAction.Delete),
        ("nak", SuffixAction.Delete), ("nek", SuffixAction.Delete),
        ("val", SuffixAction.Delete), ("vel", SuffixAction.Delete),
        ("tól", SuffixAction.Delete), ("től", SuffixAction.Delete),
        ("ról", SuffixAction.Delete), ("ről", SuffixAction.Delete),
        ("ból", SuffixAction.Delete), ("ből", SuffixAction.Delete),
        ("hoz", SuffixAction.Delete), ("hez", SuffixAction.Delete), ("höz", SuffixAction.Delete),
        ("nál", SuffixAction.Delete), ("nél", SuffixAction.Delete),
        ("ért", SuffixAction.Delete), ("kor", SuffixAction.Delete),
        ("ba", SuffixAction.Delete), ("be", SuffixAction.Delete),
        ("ra", SuffixAction.Delete), ("re", SuffixAction.Delete),
        ("ig", SuffixAction.Delete),
        ("at", SuffixAction.Delete), ("et", SuffixAction.Delete),
        ("ot", SuffixAction.Delete), ("öt", SuffixAction.Delete),
        ("ul", SuffixAction.Delete), ("ül", SuffixAction.Delete),
        ("vá", SuffixAction.Delete), ("vé", SuffixAction.Delete),
        ("en", SuffixAction.Delete), ("on", SuffixAction.Delete),
        ("an", SuffixAction.Delete), ("ön", SuffixAction.Delete),
        ("n", SuffixAction.Delete), ("t", SuffixAction.Delete),
    ];

    private static readonly (string Suffix, SuffixAction Action)[] CaseSpecialSuffixes =
    [
        ("ánként", SuffixAction.ReplaceWithA),
        ("én", SuffixAction.ReplaceWithE), ("án", SuffixAction.ReplaceWithA),
    ];

    private static readonly (string Suffix, SuffixAction Action)[] CaseOtherSuffixes =
    [
        ("astul", SuffixAction.Delete), ("estül", SuffixAction.Delete),
        ("ástul", SuffixAction.ReplaceWithA), ("éstül", SuffixAction.ReplaceWithE),
        ("stul", SuffixAction.Delete), ("stül", SuffixAction.Delete),
    ];

    private static readonly (string Suffix, SuffixAction Action)[] OwnedSuffixes =
    [
        ("oké", SuffixAction.Delete), ("öké", SuffixAction.Delete),
        ("aké", SuffixAction.Delete), ("eké", SuffixAction.Delete),
        ("éké", SuffixAction.ReplaceWithE), ("áké", SuffixAction.ReplaceWithA),
        ("ééi", SuffixAction.ReplaceWithE), ("áéi", SuffixAction.ReplaceWithA),
        ("ké", SuffixAction.Delete), ("éi", SuffixAction.Delete), ("éé", SuffixAction.ReplaceWithE),
        ("é", SuffixAction.Delete),
    ];

    private static readonly (string Suffix, SuffixAction Action)[] SingOwnerSuffixes =
    [
        ("ájuk", SuffixAction.ReplaceWithA), ("éjük", SuffixAction.ReplaceWithE),
        ("ünk", SuffixAction.Delete), ("unk", SuffixAction.Delete),
        ("ánk", SuffixAction.ReplaceWithA), ("énk", SuffixAction.ReplaceWithE),
        ("juk", SuffixAction.Delete), ("jük", SuffixAction.Delete),
        ("nk", SuffixAction.Delete),
        ("uk", SuffixAction.Delete), ("ük", SuffixAction.Delete),
        ("em", SuffixAction.Delete), ("om", SuffixAction.Delete), ("am", SuffixAction.Delete),
        ("ám", SuffixAction.ReplaceWithA), ("ém", SuffixAction.ReplaceWithE),
        ("od", SuffixAction.Delete), ("ed", SuffixAction.Delete),
        ("ad", SuffixAction.Delete), ("öd", SuffixAction.Delete),
        ("ád", SuffixAction.ReplaceWithA), ("éd", SuffixAction.ReplaceWithE),
        ("ja", SuffixAction.Delete), ("je", SuffixAction.Delete),
        ("m", SuffixAction.Delete), ("d", SuffixAction.Delete),
        ("a", SuffixAction.Delete), ("e", SuffixAction.Delete), ("o", SuffixAction.Delete),
        ("á", SuffixAction.ReplaceWithA), ("é", SuffixAction.ReplaceWithE),
    ];

    private static readonly (string Suffix, SuffixAction Action)[] PlurOwnerSuffixes =
    [
        ("jaitok", SuffixAction.Delete), ("jeitek", SuffixAction.Delete),
        ("jaink", SuffixAction.Delete), ("jeink", SuffixAction.Delete),
        ("aitok", SuffixAction.Delete), ("eitek", SuffixAction.Delete),
        ("áitok", SuffixAction.ReplaceWithA), ("éitek", SuffixAction.ReplaceWithE),
        ("jaim", SuffixAction.Delete), ("jeim", SuffixAction.Delete),
        ("jaid", SuffixAction.Delete), ("jeid", SuffixAction.Delete),
        ("eink", SuffixAction.Delete), ("aink", SuffixAction.Delete),
        ("áink", SuffixAction.ReplaceWithA), ("éink", SuffixAction.ReplaceWithE),
        ("itek", SuffixAction.Delete),
        ("jeik", SuffixAction.Delete), ("jaik", SuffixAction.Delete),
        ("áim", SuffixAction.ReplaceWithA), ("éim", SuffixAction.ReplaceWithE),
        ("aim", SuffixAction.Delete), ("eim", SuffixAction.Delete),
        ("áid", SuffixAction.ReplaceWithA), ("éid", SuffixAction.ReplaceWithE),
        ("aid", SuffixAction.Delete), ("eid", SuffixAction.Delete),
        ("jai", SuffixAction.Delete), ("jei", SuffixAction.Delete),
        ("ink", SuffixAction.Delete),
        ("aik", SuffixAction.Delete), ("eik", SuffixAction.Delete),
        ("áik", SuffixAction.ReplaceWithA), ("éik", SuffixAction.ReplaceWithE),
        ("im", SuffixAction.Delete), ("id", SuffixAction.Delete),
        ("ái", SuffixAction.ReplaceWithA), ("éi", SuffixAction.ReplaceWithE),
        ("ai", SuffixAction.Delete), ("ei", SuffixAction.Delete),
        ("ik", SuffixAction.Delete),
        ("i", SuffixAction.Delete),
    ];

    private static readonly (string Suffix, SuffixAction Action)[] PluralSuffixes =
    [
        ("ák", SuffixAction.ReplaceWithA), ("ék", SuffixAction.ReplaceWithE),
        ("ök", SuffixAction.Delete), ("ak", SuffixAction.Delete),
        ("ok", SuffixAction.Delete), ("ek", SuffixAction.Delete),
        ("k", SuffixAction.Delete),
    ];

    /// <inheritdoc />
    public string Stem(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return token;
        }

        var s = token;
        var p1 = MarkRegion(s);

        s = RemoveInstrumental(s, p1);
        s = RemoveCase(s, p1);
        s = ApplyTable(s, p1, CaseSpecialSuffixes);
        s = ApplyTable(s, p1, CaseOtherSuffixes);
        s = RemoveFactive(s, p1);
        s = ApplyTable(s, p1, OwnedSuffixes);
        s = ApplyTable(s, p1, SingOwnerSuffixes);
        s = ApplyTable(s, p1, PlurOwnerSuffixes);
        s = ApplyTable(s, p1, PluralSuffixes);
        return s;
    }

    private static bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;

    private static int MarkRegion(string s)
    {
        if (IsVowel(s[0]))
        {
            // Word starts with a vowel: R1 begins after the first non-vowel.
            for (var i = 1; i < s.Length; i++)
            {
                if (!IsVowel(s[i]))
                {
                    return i + 1;
                }
            }

            return s.Length;
        }

        // Word starts with a non-vowel: R1 begins after the first vowel.
        for (var i = 1; i < s.Length; i++)
        {
            if (IsVowel(s[i]))
            {
                return i + 1;
            }
        }

        return s.Length;
    }

    /// <summary>Tests for a doubled consonant (including digraph doubles such as ssz) ending at index <paramref name="end"/> (exclusive).</summary>
    private static bool DoubleEndsAt(string s, int end)
    {
        if (end >= 3)
        {
            var tri = s.Substring(end - 3, 3);
            if (tri is "ccs" or "ggy" or "lly" or "nny" or "ssz" or "tty" or "zzs")
            {
                return true;
            }
        }

        return end >= 2 && s[end - 1] == s[end - 2] && SimpleDoubleConsonants.IndexOf(s[end - 1]) >= 0;
    }

    /// <summary>Removes the second-to-last character (the doubled consonant left behind by assimilation).</summary>
    private static string Undouble(string s) => s.Length >= 2 ? s.Remove(s.Length - 2, 1) : s;

    private static string RemoveInstrumental(string s, int p1)
    {
        if (!s.EndsWith("al", StringComparison.Ordinal) && !s.EndsWith("el", StringComparison.Ordinal))
        {
            return s;
        }

        var start = s.Length - 2;
        if (start < p1 || !DoubleEndsAt(s, start))
        {
            return s;
        }

        return Undouble(s[..start]);
    }

    private static string RemoveCase(string s, int p1)
    {
        foreach (var (suffix, _) in CaseSuffixes)
        {
            if (!s.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var start = s.Length - suffix.Length;
            if (start < p1)
            {
                return s;
            }

            s = s[..start];

            // v_ending: normalize a trailing á/é exposed by the removal.
            if (s.Length >= 1 && s.Length - 1 >= p1)
            {
                if (s[^1] == 'á')
                {
                    s = s[..^1] + "a";
                }
                else if (s[^1] == 'é')
                {
                    s = s[..^1] + "e";
                }
            }

            return s;
        }

        return s;
    }

    private static string RemoveFactive(string s, int p1)
    {
        if (s.Length == 0 || (s[^1] != 'á' && s[^1] != 'é'))
        {
            return s;
        }

        var start = s.Length - 1;
        if (start < p1 || !DoubleEndsAt(s, start))
        {
            return s;
        }

        return Undouble(s[..start]);
    }

    private static string ApplyTable(string s, int p1, (string Suffix, SuffixAction Action)[] table)
    {
        foreach (var (suffix, action) in table)
        {
            if (!s.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var start = s.Length - suffix.Length;
            if (start < p1)
            {
                // Longest match committed; outside R1 the whole step fails.
                return s;
            }

            return action switch
            {
                SuffixAction.ReplaceWithA => s[..start] + "a",
                SuffixAction.ReplaceWithE => s[..start] + "e",
                _ => s[..start],
            };
        }

        return s;
    }
}
