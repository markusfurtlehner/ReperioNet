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

    /// <summary>
    /// Folds <paramref name="text"/> into buffers from the shared pool (2.6.2): per folded char the start of the original rune it
    /// came from. Scoring a result page folded every stored text into a new string and two lists of the length of the text, which
    /// for long mail texts was tens of megabytes per query and showed as garbage collections. Dispose it to hand the buffers back.
    /// </summary>
    internal static FoldedText FoldPooled(string text)
    {
        var chars = System.Buffers.ArrayPool<char>.Shared.Rent(text.Length + 16);
        var starts = System.Buffers.ArrayPool<int>.Shared.Rent(text.Length + 16);
        var count = 0;
        var position = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var length = rune.Utf16SequenceLength;
            if (rune.Value < FirstDecomposable)
            {
                if (count == chars.Length)
                {
                    Grow(ref chars, count);
                    Grow(ref starts, count);
                }

                chars[count] = char.ToLowerInvariant((char)rune.Value);
                starts[count++] = position;
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

                if (count == chars.Length)
                {
                    Grow(ref chars, count);
                    Grow(ref starts, count);
                }

                chars[count] = char.ToLowerInvariant(c);
                starts[count++] = position;
            }

            position += length;
        }

        return new FoldedText(text, chars, starts, count);
    }

    private static void Grow<T>(ref T[] buffer, int used)
    {
        var larger = System.Buffers.ArrayPool<T>.Shared.Rent(buffer.Length * 2);
        Array.Copy(buffer, larger, used);
        System.Buffers.ArrayPool<T>.Shared.Return(buffer);
        buffer = larger;
    }

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

/// <summary>A folded text in pooled buffers, with the place of every folded char in the original (see <see cref="TextFold.FoldPooled"/>).</summary>
internal sealed class FoldedText(string original, char[] chars, int[] starts, int length) : IDisposable
{
    private char[]? _chars = chars;
    private int[]? _starts = starts;

    /// <summary>The folded text.</summary>
    internal ReadOnlySpan<char> Span => _chars.AsSpan(0, length);

    /// <summary>Where the original rune of the folded char at <paramref name="index"/> starts.</summary>
    internal int OrigStart(int index) => _starts![index];

    /// <summary>Where the original rune of the folded char at <paramref name="index"/> ends (exclusive).</summary>
    internal int OrigEnd(int index)
    {
        var start = _starts![index];
        var pair = char.IsHighSurrogate(original[start]) && start + 1 < original.Length && char.IsLowSurrogate(original[start + 1]);
        return start + (pair ? 2 : 1);
    }

    public void Dispose()
    {
        if (_chars is { } heldChars && _starts is { } heldStarts)
        {
            _chars = null;
            _starts = null;
            System.Buffers.ArrayPool<char>.Shared.Return(heldChars);
            System.Buffers.ArrayPool<int>.Shared.Return(heldStarts);
        }
    }
}
