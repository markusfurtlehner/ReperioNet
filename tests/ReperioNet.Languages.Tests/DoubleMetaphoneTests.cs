using ReperioNet.Languages.En;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>
/// Vectors for the Double Metaphone primary code. Equality of codes across alternative spellings
/// of the same sound is the property the index relies on.
/// </summary>
public class DoubleMetaphoneTests
{
    private static readonly DoubleMetaphone Encoder = new();

    [Theory]
    [InlineData("smith", "SM0")]
    [InlineData("smyth", "SM0")]
    [InlineData("smythe", "SM0")]
    [InlineData("thomas", "TMS")]
    [InlineData("knight", "NT")]
    [InlineData("night", "NT")]
    public void Encode_MatchesKnownPrimaryCodes(string token, string expected)
        => Assert.Equal(expected, Encoder.Encode(token));

    [Theory]
    [InlineData("smith", "smyth")]
    [InlineData("knight", "night")]
    [InlineData("right", "write")]
    [InlineData("rite", "wright")]
    [InlineData("phone", "fone")]
    [InlineData("knot", "not")]
    [InlineData("philip", "filip")]
    [InlineData("catherine", "katherine")]
    public void Encode_GivesHomophonesEqualCodes(string first, string second)
    {
        var firstCode = Encoder.Encode(first);
        Assert.NotNull(firstCode);
        Assert.Equal(firstCode, Encoder.Encode(second));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("42")]
    [InlineData("")]
    public void Encode_ReturnsNullWhenNothingIsEncodable(string token)
        => Assert.Null(Encoder.Encode(token));

    [Theory]
    [InlineData("communication")]
    [InlineData("internationalization")]
    [InlineData("smith")]
    [InlineData("a")]
    public void Encode_ReturnsUppercaseCodesOfAtMostFourCharacters(string token)
    {
        var code = Encoder.Encode(token);
        Assert.NotNull(code);
        Assert.InRange(code.Length, 1, 4);
        Assert.Equal(code.ToUpperInvariant(), code);
    }
}
