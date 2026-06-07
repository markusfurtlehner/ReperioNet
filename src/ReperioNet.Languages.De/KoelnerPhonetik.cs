using System.Text;
using ReperioNet.Abstractions;

namespace ReperioNet.Languages.De;

/// <summary>
/// Standard Kölner Phonetik (H. J. Postel, 1969): each letter maps to a digit 0–8 depending on its
/// neighbours, consecutive identical codes are collapsed, and every '0' except a leading one is
/// dropped. Tailored to lowercase German tokens: ä/ö/ü/y count as vowels (code 0) and ß as s
/// (code 8).
/// </summary>
/// <remarks>
/// Thread-safe: the type is stateless and all working state lives in locals, so
/// <see cref="Encode"/> may be called concurrently.
/// </remarks>
public sealed class KoelnerPhonetik : IPhoneticEncoder
{
    /// <inheritdoc />
    public string? Encode(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        // Normalize so the context rules below only ever see plain ASCII letters:
        // ä→a, ö→o, ü→u (vowels) and ß→s. Anything unmapped passes through and simply
        // produces no code in MapLetter.
        var letters = new char[token.Length];
        for (var i = 0; i < token.Length; i++)
        {
            letters[i] = token[i] switch
            {
                'ä' => 'a',
                'ö' => 'o',
                'ü' => 'u',
                'ß' => 's',
                var c => c,
            };
        }

        // Raw digit sequence with consecutive identical codes collapsed as they are produced.
        // 'h' and non-letters contribute nothing, so codes around them become adjacent (and may
        // collapse), per the standard procedure.
        var collapsed = new StringBuilder(letters.Length);
        for (var i = 0; i < letters.Length; i++)
        {
            var previous = i > 0 ? letters[i - 1] : '\0';
            var next = i + 1 < letters.Length ? letters[i + 1] : '\0';
            var code = MapLetter(letters[i], previous, next, isInitial: i == 0);
            foreach (var digit in code)
            {
                if (collapsed.Length == 0 || collapsed[collapsed.Length - 1] != digit)
                {
                    collapsed.Append(digit);
                }
            }
        }

        if (collapsed.Length == 0)
        {
            return null;
        }

        // Drop every '0' except in leading position.
        var result = new StringBuilder(collapsed.Length);
        for (var i = 0; i < collapsed.Length; i++)
        {
            if (i == 0 || collapsed[i] != '0')
            {
                result.Append(collapsed[i]);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// The context-sensitive letter-to-code table. <paramref name="previous"/> and
    /// <paramref name="next"/> are the adjacent letters of the normalized token ('\0' at the
    /// boundaries); <paramref name="isInitial"/> marks the first letter (Anlaut).
    /// </summary>
    private static string MapLetter(char letter, char previous, char next, bool isInitial)
    {
        switch (letter)
        {
            case 'a' or 'e' or 'i' or 'j' or 'o' or 'u' or 'y':
                return "0";
            case 'h':
                return string.Empty;
            case 'b':
                return "1";
            case 'p':
                return next == 'h' ? "3" : "1";
            case 'd' or 't':
                return next is 'c' or 's' or 'z' ? "8" : "2";
            case 'f' or 'v' or 'w':
                return "3";
            case 'g' or 'k' or 'q':
                return "4";
            case 'c':
                if (isInitial)
                {
                    return next is 'a' or 'h' or 'k' or 'l' or 'o' or 'q' or 'r' or 'u' or 'x' ? "4" : "8";
                }

                if (previous is 's' or 'z')
                {
                    return "8";
                }

                return next is 'a' or 'h' or 'k' or 'o' or 'q' or 'u' or 'x' ? "4" : "8";
            case 'x':
                return previous is 'c' or 'k' or 'q' ? "8" : "48";
            case 'l':
                return "5";
            case 'm' or 'n':
                return "6";
            case 'r':
                return "7";
            case 's' or 'z':
                return "8";
            default:
                // Digits and letters outside the German alphabet are not encodable.
                return string.Empty;
        }
    }
}
