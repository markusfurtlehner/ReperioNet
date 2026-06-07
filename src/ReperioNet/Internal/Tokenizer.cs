using System.Text;

namespace ReperioNet.Internal;

/// <summary>
/// The C# tokenize helper (PRD §15.3, binding): used only to split a query into terms for the MATCH
/// expression and, in later milestones, to feed the stemmer/phonetic encoder. It is NOT used to
/// produce the <c>base</c> FTS content — raw text goes into <c>documents_fts.base</c> and the
/// <c>unicode61 remove_diacritics 2</c> tokenizer owns folding/splitting on both sides.
/// </summary>
internal static class Tokenizer
{
    /// <summary>
    /// Enumerates Unicode runes, splits on any rune that is not a letter or digit, lowercases each
    /// token invariantly and returns the non-empty tokens in order. Diacritics are NOT stripped here
    /// (FTS5 folds them on both index and query side).
    /// </summary>
    internal static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        var current = new StringBuilder();
        Span<char> utf16 = stackalloc char[2];
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                current.Append(utf16[..rune.EncodeToUtf16(utf16)]);
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString().ToLowerInvariant());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString().ToLowerInvariant());
        }

        return tokens;
    }
}
