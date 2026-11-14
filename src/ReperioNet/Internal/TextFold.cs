using System.Globalization;
using System.Text;

namespace ReperioNet.Internal;

/// <summary>
/// C#-side diacritic folding + lowercasing, used for the §9.11 exact-match boost and §9.13 snippet
/// matching (both defined over "diacritic-folded, lowercased" text). NFD-decomposes each rune,
/// drops combining marks and lowercases invariantly.
/// </summary>
internal static class TextFold
{
    /// <summary>Returns the diacritic-folded, lowercased form of <paramref name="text"/>.</summary>
    internal static string Fold(string text) => FoldCore(text, null, null);

    /// <summary>
    /// Folds <paramref name="text"/> and returns, per folded char, the start index and exclusive end
    /// index of the original rune it came from — so folded match positions map back to original spans.
    /// </summary>
    internal static (string Folded, List<int> OrigStart, List<int> OrigEndExclusive) FoldWithMap(string text)
    {
        var starts = new List<int>(text.Length);
        var ends = new List<int>(text.Length);
        var folded = FoldCore(text, starts, ends);
        return (folded, starts, ends);
    }

    /// <summary>The first code point with a canonical decomposition (U+00C0, À).</summary>
    private const int FirstDecomposable = 0xC0;

    /// <summary>
    /// The decompositions of the Latin letters up to U+024F, kept once they were asked for. Two threads may fill an entry at the
    /// same time; both write the same text.
    /// </summary>
    private static readonly string?[] LatinFolds = new string?[0x250];

    private static string FoldCore(string text, List<int>? origStart, List<int>? origEndExclusive)
    {
        var builder = new StringBuilder(text.Length);
        var position = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var length = rune.Utf16SequenceLength;
            if (rune.Value < FirstDecomposable)
            {
                // 2.6.2: below U+00C0 nothing decomposes and nothing is a combining mark, so the fold is the lowercase letter.
                // Normalizing every character on its own cost a string per character, twice per scored text.
                builder.Append(char.ToLowerInvariant((char)rune.Value));
                origStart?.Add(position);
                origEndExclusive?.Add(position + length);
                position += length;
                continue;
            }

            var decomposed = rune.Value < LatinFolds.Length
                ? LatinFolds[rune.Value] ??= rune.ToString().Normalize(NormalizationForm.FormD)
                : rune.ToString().Normalize(NormalizationForm.FormD);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                builder.Append(char.ToLowerInvariant(c));
                origStart?.Add(position);
                origEndExclusive?.Add(position + length);
            }

            position += length;
        }

        return builder.ToString();
    }
}
