// Port of Lawrence Philips' Double Metaphone phonetic encoding algorithm
// (C/C++ Users Journal, June 2000). Only the PRIMARY code is produced; the
// alternate code of the original algorithm is not used by this encoder.
using System.Text;
using ReperioNet.Abstractions;

namespace ReperioNet.Languages.En;

/// <summary>
/// Lawrence Philips' Double Metaphone phonetic encoder for English. <see cref="Encode"/> returns the
/// PRIMARY Double Metaphone code (uppercase, at most four characters); the alternate code of the
/// original algorithm is not computed or used. Stateless and safe for concurrent use.
/// </summary>
public sealed class DoubleMetaphone : IPhoneticEncoder
{
    /// <summary>The maximum length of a returned code.</summary>
    private const int MaxCodeLength = 4;

    /// <summary>
    /// Returns the primary Double Metaphone code for <paramref name="token"/>, or <see langword="null"/>
    /// when nothing is encodable (for example a digits-only token).
    /// </summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    /// <returns>The uppercase primary code (1-4 characters), or <see langword="null"/>.</returns>
    public string? Encode(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.Length == 0)
        {
            return null;
        }

        var word = token.ToUpperInvariant();
        var length = word.Length;
        var last = length - 1;
        var slavoGermanic = IsSlavoGermanic(word);
        var primary = new StringBuilder(MaxCodeLength + 2);
        var current = 0;

        // Skip a silent first letter, e.g. 'gnome', 'knight', 'pneumonia', 'wrack', 'psychology'.
        if (StringAt(word, 0, "GN", "KN", "PN", "WR", "PS"))
        {
            current = 1;
        }

        // An initial X is pronounced Z, which maps to S, e.g. 'Xavier'.
        if (CharAt(word, 0) == 'X')
        {
            primary.Append('S');
            current = 1;
        }

        while (primary.Length < MaxCodeLength && current < length)
        {
            switch (CharAt(word, current))
            {
                case 'A':
                case 'E':
                case 'I':
                case 'O':
                case 'U':
                case 'Y':
                    if (current == 0)
                    {
                        primary.Append('A');
                    }

                    current++;
                    break;

                case 'B':
                    // "-mb", e.g. "dumb", is already skipped over in the M case.
                    primary.Append('P');
                    current += CharAt(word, current + 1) == 'B' ? 2 : 1;
                    break;

                case 'Ç':
                    primary.Append('S');
                    current++;
                    break;

                case 'C':
                    current = EncodeC(word, current, last, primary);
                    break;

                case 'D':
                    current = EncodeD(word, current, primary);
                    break;

                case 'F':
                    current += CharAt(word, current + 1) == 'F' ? 2 : 1;
                    primary.Append('F');
                    break;

                case 'G':
                    current = EncodeG(word, current, slavoGermanic, primary);
                    break;

                case 'H':
                    // Keep an H only if first or between two vowels (also drops 'HH').
                    if ((current == 0 || IsVowel(word, current - 1)) && IsVowel(word, current + 1))
                    {
                        primary.Append('H');
                        current += 2;
                    }
                    else
                    {
                        current++;
                    }

                    break;

                case 'J':
                    current = EncodeJ(word, current, last, slavoGermanic, primary);
                    break;

                case 'K':
                    current += CharAt(word, current + 1) == 'K' ? 2 : 1;
                    primary.Append('K');
                    break;

                case 'L':
                    current = EncodeL(word, current, length, last, primary);
                    break;

                case 'M':
                    if ((StringAt(word, current - 1, "UMB")
                            && (current + 1 == last || StringAt(word, current + 2, "ER")))
                        || CharAt(word, current + 1) == 'M')
                    {
                        // e.g. 'dumb', 'thumb'.
                        current += 2;
                    }
                    else
                    {
                        current++;
                    }

                    primary.Append('M');
                    break;

                case 'N':
                    current += CharAt(word, current + 1) == 'N' ? 2 : 1;
                    primary.Append('N');
                    break;

                case 'Ñ':
                    current++;
                    primary.Append('N');
                    break;

                case 'P':
                    if (CharAt(word, current + 1) == 'H')
                    {
                        primary.Append('F');
                        current += 2;
                        break;
                    }

                    // Also account for "campbell" and "raspberry".
                    current += StringAt(word, current + 1, "P", "B") ? 2 : 1;
                    primary.Append('P');
                    break;

                case 'Q':
                    current += CharAt(word, current + 1) == 'Q' ? 2 : 1;
                    primary.Append('K');
                    break;

                case 'R':
                    // French final -ier, e.g. 'rogier', is dropped in the primary (but not 'hochmeier').
                    if (!(current == last
                        && !slavoGermanic
                        && StringAt(word, current - 2, "IE")
                        && !StringAt(word, current - 4, "ME", "MA")))
                    {
                        primary.Append('R');
                    }

                    current += CharAt(word, current + 1) == 'R' ? 2 : 1;
                    break;

                case 'S':
                    current = EncodeS(word, current, last, slavoGermanic, primary);
                    break;

                case 'T':
                    current = EncodeT(word, current, primary);
                    break;

                case 'V':
                    current += CharAt(word, current + 1) == 'V' ? 2 : 1;
                    primary.Append('F');
                    break;

                case 'W':
                    current = EncodeW(word, current, last, primary);
                    break;

                case 'X':
                    // French final -aux/-eaux, e.g. 'breaux', is silent.
                    if (!(current == last
                        && (StringAt(word, current - 3, "IAU", "EAU") || StringAt(word, current - 2, "AU", "OU"))))
                    {
                        primary.Append("KS");
                    }

                    current += StringAt(word, current + 1, "C", "X") ? 2 : 1;
                    break;

                case 'Z':
                    // Chinese pinyin, e.g. 'zhao'.
                    if (CharAt(word, current + 1) == 'H')
                    {
                        primary.Append('J');
                        current += 2;
                        break;
                    }

                    // The primary is S in all remaining cases (the TS variant only affects the alternate).
                    primary.Append('S');
                    current += CharAt(word, current + 1) == 'Z' ? 2 : 1;
                    break;

                default:
                    current++;
                    break;
            }
        }

        if (primary.Length == 0)
        {
            return null;
        }

        return primary.Length <= MaxCodeLength ? primary.ToString() : primary.ToString(0, MaxCodeLength);
    }

    /// <summary>Encodes a C at <paramref name="current"/> and returns the next position.</summary>
    private static int EncodeC(string word, int current, int last, StringBuilder primary)
    {
        // Various Germanic spellings, e.g. 'macher'.
        if (current > 1
            && !IsVowel(word, current - 2)
            && StringAt(word, current - 1, "ACH")
            && CharAt(word, current + 2) != 'I'
            && (CharAt(word, current + 2) != 'E' || StringAt(word, current - 2, "BACHER", "MACHER")))
        {
            primary.Append('K');
            return current + 2;
        }

        // Special case 'caesar'.
        if (current == 0 && StringAt(word, current, "CAESAR"))
        {
            primary.Append('S');
            return current + 2;
        }

        // Italian 'chianti'.
        if (StringAt(word, current, "CHIA"))
        {
            primary.Append('K');
            return current + 2;
        }

        if (StringAt(word, current, "CH"))
        {
            // 'michael'.
            if (current > 0 && StringAt(word, current, "CHAE"))
            {
                primary.Append('K');
                return current + 2;
            }

            // Greek roots, e.g. 'chemistry', 'chorus'.
            if (current == 0
                && (StringAt(word, current + 1, "HARAC", "HARIS") || StringAt(word, current + 1, "HOR", "HYM", "HIA", "HEM"))
                && !StringAt(word, 0, "CHORE"))
            {
                primary.Append('K');
                return current + 2;
            }

            // Germanic, Greek, or otherwise 'ch' for the 'kh' sound.
            if (StringAt(word, 0, "VAN ", "VON ")
                || StringAt(word, 0, "SCH")
                // 'architect' but not 'arch'; 'orchestra', 'orchid'.
                || StringAt(word, current - 2, "ORCHES", "ARCHIT", "ORCHID")
                || StringAt(word, current + 2, "T", "S")
                || ((StringAt(word, current - 1, "A", "O", "U", "E") || current == 0)
                    // e.g. 'wachtler', 'wechsler', but not 'tichner'.
                    && StringAt(word, current + 2, "L", "R", "N", "M", "B", "H", "F", "V", "W", " ")))
            {
                primary.Append('K');
            }
            else if (current > 0)
            {
                // e.g. "McHugh".
                primary.Append(StringAt(word, 0, "MC") ? 'K' : 'X');
            }
            else
            {
                primary.Append('X');
            }

            return current + 2;
        }

        // e.g. 'czerny'.
        if (StringAt(word, current, "CZ") && !StringAt(word, current - 2, "WICZ"))
        {
            primary.Append('S');
            return current + 2;
        }

        // e.g. 'focaccia'.
        if (StringAt(word, current + 1, "CIA"))
        {
            primary.Append('X');
            return current + 3;
        }

        // Double C, but not if e.g. 'McClellan'.
        if (StringAt(word, current, "CC") && !(current == 1 && CharAt(word, 0) == 'M'))
        {
            // 'bellocchio' but not 'bacchus'.
            if (StringAt(word, current + 2, "I", "E", "H") && !StringAt(word, current + 2, "HU"))
            {
                // 'accident', 'accede', 'succeed'.
                if ((current == 1 && CharAt(word, current - 1) == 'A')
                    || StringAt(word, current - 1, "UCCEE", "UCCES"))
                {
                    primary.Append("KS");
                }
                else
                {
                    // 'bacci', 'bertucci' and other Italian spellings.
                    primary.Append('X');
                }

                return current + 3;
            }

            // Pierce's rule.
            primary.Append('K');
            return current + 2;
        }

        if (StringAt(word, current, "CK", "CG", "CQ"))
        {
            primary.Append('K');
            return current + 2;
        }

        if (StringAt(word, current, "CI", "CE", "CY"))
        {
            primary.Append('S');
            return current + 2;
        }

        primary.Append('K');

        // Name sent in 'mac caffrey', 'mac gregor'.
        if (StringAt(word, current + 1, " C", " Q", " G"))
        {
            return current + 3;
        }

        if (StringAt(word, current + 1, "C", "K", "Q") && !StringAt(word, current + 1, "CE", "CI"))
        {
            return current + 2;
        }

        return current + 1;
    }

    /// <summary>Encodes a D at <paramref name="current"/> and returns the next position.</summary>
    private static int EncodeD(string word, int current, StringBuilder primary)
    {
        if (StringAt(word, current, "DG"))
        {
            if (StringAt(word, current + 2, "I", "E", "Y"))
            {
                // e.g. 'edge'.
                primary.Append('J');
                return current + 3;
            }

            // e.g. 'edgar'.
            primary.Append("TK");
            return current + 2;
        }

        if (StringAt(word, current, "DT", "DD"))
        {
            primary.Append('T');
            return current + 2;
        }

        primary.Append('T');
        return current + 1;
    }

    /// <summary>Encodes a G at <paramref name="current"/> and returns the next position.</summary>
    private static int EncodeG(string word, int current, bool slavoGermanic, StringBuilder primary)
    {
        if (CharAt(word, current + 1) == 'H')
        {
            if (current > 0 && !IsVowel(word, current - 1))
            {
                primary.Append('K');
                return current + 2;
            }

            // 'ghislane', 'ghiradelli'.
            if (current == 0)
            {
                primary.Append(CharAt(word, current + 2) == 'I' ? 'J' : 'K');
                return current + 2;
            }

            // Parker's rule (with some further refinements), e.g. 'hugh', 'bough', 'broughton'.
            if ((current > 1 && StringAt(word, current - 2, "B", "H", "D"))
                || (current > 2 && StringAt(word, current - 3, "B", "H", "D"))
                || (current > 3 && StringAt(word, current - 4, "B", "H")))
            {
                return current + 2;
            }

            // e.g. 'laugh', 'McLaughlin', 'cough', 'gough', 'rough', 'tough'.
            if (current > 2 && CharAt(word, current - 1) == 'U' && StringAt(word, current - 3, "C", "G", "L", "R", "T"))
            {
                primary.Append('F');
            }
            else if (current > 0 && CharAt(word, current - 1) != 'I')
            {
                primary.Append('K');
            }

            return current + 2;
        }

        if (CharAt(word, current + 1) == 'N')
        {
            if (current == 1 && IsVowel(word, 0) && !slavoGermanic)
            {
                primary.Append("KN");
            }
            else if (!StringAt(word, current + 2, "EY") && CharAt(word, current + 1) != 'Y' && !slavoGermanic)
            {
                // Not e.g. 'cagney'.
                primary.Append('N');
            }
            else
            {
                primary.Append("KN");
            }

            return current + 2;
        }

        // 'tagliaro'.
        if (StringAt(word, current + 1, "LI") && !slavoGermanic)
        {
            primary.Append("KL");
            return current + 2;
        }

        // -ges-, -gep-, -gel-, -gie- at the beginning.
        if (current == 0
            && (CharAt(word, current + 1) == 'Y'
                || StringAt(word, current + 1, "ES", "EP", "EB", "EL", "EY", "IB", "IL", "IN", "IE", "EI", "ER")))
        {
            primary.Append('K');
            return current + 2;
        }

        // -ger-, -gy-.
        if ((StringAt(word, current + 1, "ER") || CharAt(word, current + 1) == 'Y')
            && !StringAt(word, 0, "DANGER", "RANGER", "MANGER")
            && !StringAt(word, current - 1, "E", "I")
            && !StringAt(word, current - 1, "RGY", "OGY"))
        {
            primary.Append('K');
            return current + 2;
        }

        // Italian, e.g. 'biaggi'.
        if (StringAt(word, current + 1, "E", "I", "Y") || StringAt(word, current - 1, "AGGI", "OGGI"))
        {
            // Obvious Germanic.
            if (StringAt(word, 0, "VAN ", "VON ") || StringAt(word, 0, "SCH") || StringAt(word, current + 1, "ET"))
            {
                primary.Append('K');
            }
            else
            {
                // Always soft if a French ending; otherwise J in the primary.
                primary.Append('J');
            }

            return current + 2;
        }

        primary.Append('K');
        return current + (CharAt(word, current + 1) == 'G' ? 2 : 1);
    }

    /// <summary>Encodes a J at <paramref name="current"/> and returns the next position.</summary>
    private static int EncodeJ(string word, int current, int last, bool slavoGermanic, StringBuilder primary)
    {
        // Obvious Spanish, 'jose', 'san jacinto'.
        if (StringAt(word, current, "JOSE") || StringAt(word, 0, "SAN "))
        {
            primary.Append((current == 0 && CharAt(word, current + 4) == ' ') || StringAt(word, 0, "SAN ") ? 'H' : 'J');
            return current + 1;
        }

        if (current == 0)
        {
            // Yankelovich/Jankelowicz: primary J.
            primary.Append('J');
        }
        else if (IsVowel(word, current - 1)
            && !slavoGermanic
            && (CharAt(word, current + 1) == 'A' || CharAt(word, current + 1) == 'O'))
        {
            // Spanish pronunciation of e.g. 'bajador': primary J.
            primary.Append('J');
        }
        else if (current == last)
        {
            primary.Append('J');
        }
        else if (!StringAt(word, current + 1, "L", "T", "K", "S", "N", "M", "B", "Z")
            && !StringAt(word, current - 1, "S", "K", "L"))
        {
            primary.Append('J');
        }

        return current + (CharAt(word, current + 1) == 'J' ? 2 : 1);
    }

    /// <summary>Encodes an L at <paramref name="current"/> and returns the next position.</summary>
    private static int EncodeL(string word, int current, int length, int last, StringBuilder primary)
    {
        if (CharAt(word, current + 1) == 'L')
        {
            // Spanish, e.g. 'cabrillo', 'gallegos' (silent in the alternate only).
            if ((current == length - 3 && StringAt(word, current - 1, "ILLO", "ILLA", "ALLE"))
                || ((StringAt(word, last - 1, "AS", "OS") || StringAt(word, last, "A", "O"))
                    && StringAt(word, current - 1, "ALLE")))
            {
                primary.Append('L');
                return current + 2;
            }

            primary.Append('L');
            return current + 2;
        }

        primary.Append('L');
        return current + 1;
    }

    /// <summary>Encodes an S at <paramref name="current"/> and returns the next position.</summary>
    private static int EncodeS(string word, int current, int last, bool slavoGermanic, StringBuilder primary)
    {
        // Special cases 'island', 'isle', 'carlisle', 'carlysle': silent S.
        if (StringAt(word, current - 1, "ISL", "YSL"))
        {
            return current + 1;
        }

        // Special case 'sugar-'.
        if (current == 0 && StringAt(word, current, "SUGAR"))
        {
            primary.Append('X');
            return current + 1;
        }

        if (StringAt(word, current, "SH"))
        {
            // Germanic names, e.g. 'sholz'.
            primary.Append(StringAt(word, current + 1, "HEIM", "HOEK", "HOLM", "HOLZ") ? 'S' : 'X');
            return current + 2;
        }

        // Italian and Armenian, e.g. 'sio', 'sia'.
        if (StringAt(word, current, "SIO", "SIA") || StringAt(word, current, "SIAN"))
        {
            primary.Append('S');
            return current + 3;
        }

        // German and Anglicisations, e.g. 'smith' matching 'schmidt', 'snider' matching 'schneider';
        // also -sz- in Slavic spellings.
        if ((current == 0 && StringAt(word, current + 1, "M", "N", "L", "W")) || StringAt(word, current + 1, "Z"))
        {
            primary.Append('S');
            return current + (StringAt(word, current + 1, "Z") ? 2 : 1);
        }

        if (StringAt(word, current, "SC"))
        {
            // Schlesinger's rule.
            if (CharAt(word, current + 2) == 'H')
            {
                // Dutch origin, e.g. 'school', 'schooner'.
                if (StringAt(word, current + 3, "OO", "ER", "EN", "UY", "ED", "EM"))
                {
                    // 'schermerhorn', 'schenker': primary X.
                    primary.Append(StringAt(word, current + 3, "ER", "EN") ? "X" : "SK");
                    return current + 3;
                }

                primary.Append('X');
                return current + 3;
            }

            if (StringAt(word, current + 2, "I", "E", "Y"))
            {
                primary.Append('S');
                return current + 3;
            }

            primary.Append("SK");
            return current + 3;
        }

        // French, e.g. 'resnais', 'artois': final S silent in the primary.
        if (!(current == last && StringAt(word, current - 2, "AI", "OI")))
        {
            primary.Append('S');
        }

        return current + (StringAt(word, current + 1, "S", "Z") ? 2 : 1);
    }

    /// <summary>Encodes a T at <paramref name="current"/> and returns the next position.</summary>
    private static int EncodeT(string word, int current, StringBuilder primary)
    {
        if (StringAt(word, current, "TION"))
        {
            primary.Append('X');
            return current + 3;
        }

        if (StringAt(word, current, "TIA", "TCH"))
        {
            primary.Append('X');
            return current + 3;
        }

        if (StringAt(word, current, "TH") || StringAt(word, current, "TTH"))
        {
            // Special case 'thomas', 'thames', or Germanic.
            if (StringAt(word, current + 2, "OM", "AM") || StringAt(word, 0, "VAN ", "VON ") || StringAt(word, 0, "SCH"))
            {
                primary.Append('T');
            }
            else
            {
                primary.Append('0');
            }

            return current + 2;
        }

        primary.Append('T');
        return current + (StringAt(word, current + 1, "T", "D") ? 2 : 1);
    }

    /// <summary>Encodes a W at <paramref name="current"/> and returns the next position.</summary>
    private static int EncodeW(string word, int current, int last, StringBuilder primary)
    {
        // WR can also occur in the middle of a word.
        if (StringAt(word, current, "WR"))
        {
            primary.Append('R');
            return current + 2;
        }

        if (current == 0 && (IsVowel(word, current + 1) || StringAt(word, current, "WH")))
        {
            // 'Wasserman' should match 'Vasserman'; 'Uomo' should match 'Womo'.
            primary.Append('A');
        }

        // 'Arnow' should match 'Arnoff' (the F variant only affects the alternate).
        if ((current == last && IsVowel(word, current - 1))
            || StringAt(word, current - 1, "EWSKI", "EWSKY", "OWSKI", "OWSKY")
            || StringAt(word, 0, "SCH"))
        {
            return current + 1;
        }

        // Polish, e.g. 'filipowicz'.
        if (StringAt(word, current, "WICZ", "WITZ"))
        {
            primary.Append("TS");
            return current + 4;
        }

        return current + 1;
    }

    /// <summary>True when the word looks Slavo-Germanic (contains W, K, CZ or WITZ).</summary>
    private static bool IsSlavoGermanic(string word)
        => word.Contains('W')
            || word.Contains('K')
            || word.Contains("CZ", StringComparison.Ordinal)
            || word.Contains("WITZ", StringComparison.Ordinal);

    /// <summary>
    /// Returns the character at <paramref name="index"/>; positions past the end read as the space
    /// padding of the original algorithm, positions before the start as NUL.
    /// </summary>
    private static char CharAt(string word, int index)
    {
        if (index < 0)
        {
            return '\0';
        }

        return index < word.Length ? word[index] : ' ';
    }

    /// <summary>True when the character at <paramref name="index"/> is one of A, E, I, O, U, Y.</summary>
    private static bool IsVowel(string word, int index)
        => CharAt(word, index) is 'A' or 'E' or 'I' or 'O' or 'U' or 'Y';

    /// <summary>True when the text at <paramref name="start"/> matches any of <paramref name="candidates"/> (with end-of-word space padding).</summary>
    private static bool StringAt(string word, int start, params string[] candidates)
    {
        if (start < 0)
        {
            return false;
        }

        foreach (var candidate in candidates)
        {
            if (Matches(word, start, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="candidate"/> occurs at <paramref name="start"/> (with end-of-word space padding).</summary>
    private static bool Matches(string word, int start, string candidate)
    {
        for (var i = 0; i < candidate.Length; i++)
        {
            if (CharAt(word, start + i) != candidate[i])
            {
                return false;
            }
        }

        return true;
    }
}
