using ReperioNet.Internal;
using Xunit;

namespace ReperioNet.Tests;

public class TextFoldTests
{
    [Theory]
    [InlineData("MÜLLER", "muller")]
    [InlineData("Crème Brûlée", "creme brulee")]
    [InlineData("ångström", "angstrom")]
    [InlineData("abc123 !?", "abc123 !?")]
    [InlineData("", "")]
    public void Fold_RemovesDiacriticsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, TextFold.Fold(input));
    }

    [Fact]
    public void FoldWithMap_MapsFoldedPositionsToOriginalSpans()
    {
        var (folded, origStart, origEnd) = TextFold.FoldWithMap("Crème");

        Assert.Equal("creme", folded);
        Assert.Equal([0, 1, 2, 3, 4], origStart);
        Assert.Equal([1, 2, 3, 4, 5], origEnd);
    }

    [Fact]
    public void FoldWithMap_SurrogatePairs_MapToTheWholeRuneSpan()
    {
        // "𝐀" (U+1D400) has no decomposition or case mapping; its two UTF-16 chars are kept and
        // both must map back to the rune's full original span [1, 3).
        var (folded, origStart, origEnd) = TextFold.FoldWithMap("a𝐀b");

        Assert.Equal("a𝐀b", folded);
        Assert.Equal([0, 1, 1, 3], origStart);
        Assert.Equal([1, 3, 3, 4], origEnd);
    }
    [Fact]
    public void Fold_OfEveryCharacterUpToU0400_IsItsDecompositionWithoutMarksLowercased()
    {
        // 2.6.2 folds the characters below U+00C0 and the Latin letters up to U+024F on a short path; the result must be the one
        // the definition gives. Asked twice, so the kept decompositions are read as well as filled.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var code = 0; code < 0x400; code++)
            {
                var text = ((char)code).ToString();
                var expected = string.Concat(text.Normalize(System.Text.NormalizationForm.FormD)
                    .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    .Select(char.ToLowerInvariant));

                Assert.Equal(expected, TextFold.Fold(text));
            }
        }
    }
}
