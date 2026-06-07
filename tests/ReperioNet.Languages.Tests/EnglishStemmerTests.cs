using ReperioNet.Languages.En;
using Xunit;

namespace ReperioNet.Languages.Tests;

/// <summary>
/// Vectors for the Snowball "english" (Porter2) stemmer. All expected stems were verified against
/// the official Snowball reference implementation and its published sample vocabulary.
/// </summary>
public class EnglishStemmerTests
{
    private static readonly SnowballEnglishStemmer Stemmer = new();

    [Theory]
    // Step 1b / short-syllable rules.
    [InlineData("running", "run")]
    [InlineData("runs", "run")]
    [InlineData("knitting", "knit")]
    [InlineData("hopping", "hop")]
    [InlineData("hoped", "hope")]
    [InlineData("hoping", "hope")]
    [InlineData("agreed", "agre")]
    [InlineData("feed", "feed")]
    // Step 1a.
    [InlineData("flies", "fli")]
    [InlineData("ties", "tie")]
    [InlineData("cries", "cri")]
    [InlineData("gas", "gas")]
    [InlineData("gaps", "gap")]
    [InlineData("kiwis", "kiwi")]
    // Steps 2-5.
    [InlineData("consign", "consign")]
    [InlineData("consigned", "consign")]
    [InlineData("consigning", "consign")]
    [InlineData("consignment", "consign")]
    [InlineData("relational", "relat")]
    [InlineData("conditional", "condit")]
    [InlineData("rational", "ration")]
    [InlineData("adjustable", "adjust")]
    [InlineData("adjustment", "adjust")]
    [InlineData("replacement", "replac")]
    [InlineData("hopefulness", "hope")]
    [InlineData("goodness", "good")]
    // Special R1 prefixes.
    [InlineData("generate", "generat")]
    [InlineData("general", "general")]
    [InlineData("generous", "generous")]
    [InlineData("communication", "communic")]
    [InlineData("arsenal", "arsenal")]
    [InlineData("interesting", "interest")]
    public void Stem_MatchesOfficialVectors(string token, string expected)
        => Assert.Equal(expected, Stemmer.Stem(token));

    [Theory]
    // Exceptional word forms.
    [InlineData("skis", "ski")]
    [InlineData("skies", "sky")]
    [InlineData("dying", "die")]
    [InlineData("lying", "lie")]
    [InlineData("tying", "tie")]
    [InlineData("early", "earli")]
    [InlineData("only", "onli")]
    // Invariant forms.
    [InlineData("sky", "sky")]
    [InlineData("news", "news")]
    [InlineData("bias", "bias")]
    public void Stem_HandlesExceptionalForms(string token, string expected)
        => Assert.Equal(expected, Stemmer.Stem(token));

    [Theory]
    [InlineData("inning")]
    [InlineData("outing")]
    [InlineData("canning")]
    [InlineData("herring")]
    [InlineData("earring")]
    [InlineData("proceed")]
    [InlineData("exceed")]
    [InlineData("succeed")]
    public void Stem_LeavesPostStep1AInvariantsUnchanged(string token)
        => Assert.Equal(token, Stemmer.Stem(token));

    [Theory]
    [InlineData("connected", "connecting")]
    [InlineData("electricity", "electrical")]
    [InlineData("flies", "fly")]
    [InlineData("rolling", "rolled")]
    public void Stem_MapsInflectionsOfOneLemmaToTheSameStem(string first, string second)
        => Assert.Equal(Stemmer.Stem(first), Stemmer.Stem(second));

    [Theory]
    [InlineData("running")]
    [InlineData("consignment")]
    [InlineData("generously")]
    [InlineData("hopefulness")]
    [InlineData("skies")]
    public void Stem_IsIdempotentOnItsOwnOutput(string token)
    {
        var stem = Stemmer.Stem(token);
        Assert.Equal(stem, Stemmer.Stem(stem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("be")]
    [InlineData("ox")]
    public void Stem_LeavesShortTokensUnchanged(string token)
        => Assert.Equal(token, Stemmer.Stem(token));
}
