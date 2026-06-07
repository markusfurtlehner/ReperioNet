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

    private static string FoldCore(string text, List<int>? origStart, List<int>? origEndExclusive)
    {
        var builder = new StringBuilder(text.Length);
        var position = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var length = rune.Utf16SequenceLength;
            var decomposed = rune.ToString().Normalize(NormalizationForm.FormD);
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
