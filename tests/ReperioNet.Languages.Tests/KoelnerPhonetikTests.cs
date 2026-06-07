using ReperioNet.Languages.De;
using Xunit;

namespace ReperioNet.Languages.Tests;

public sealed class KoelnerPhonetikTests
{
    private static readonly KoelnerPhonetik Encoder = new();

    [Theory]
    [InlineData("müller", "657")]
    [InlineData("mueller", "657")]
    [InlineData("meier", "67")]
    [InlineData("maier", "67")]
    [InlineData("mayer", "67")]
    [InlineData("meyer", "67")]
    [InlineData("schmidt", "862")]
    [InlineData("schmitt", "862")]
    [InlineData("breschnew", "17863")]
    [InlineData("wikipedia", "3412")]
    [InlineData("philipp", "351")]
    [InlineData("filip", "351")]
    [InlineData("weiß", "38")]
    [InlineData("weiss", "38")]
    // A leading vowel keeps its 0; only non-leading zeros are dropped.
    [InlineData("heinz", "068")]
    public void Encode_ProducesCanonicalCode(string token, string expected)
        => Assert.Equal(expected, Encoder.Encode(token));

    [Theory]
    [InlineData("müller", "mueller")]
    [InlineData("meier", "mayer")]
    [InlineData("maier", "meyer")]
    [InlineData("schmidt", "schmitt")]
    [InlineData("philipp", "filip")]
    [InlineData("weiß", "weiss")]
    [InlineData("cäsar", "kaiser")]
    [InlineData("stadt", "statt")]
    public void Encode_SpellingVariants_ShareOneCode(string first, string second)
    {
        var firstCode = Encoder.Encode(first);

        Assert.NotNull(firstCode);
        Assert.Equal(firstCode, Encoder.Encode(second));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("42")]
    [InlineData("")]
    public void Encode_NonEncodableTokens_ReturnNull(string token)
        => Assert.Null(Encoder.Encode(token));
}
