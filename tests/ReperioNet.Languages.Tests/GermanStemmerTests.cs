using ReperioNet.Languages.De;
using Xunit;

namespace ReperioNet.Languages.Tests;

public sealed class GermanStemmerTests
{
    private static readonly SnowballGermanStemmer Stemmer = new();

    [Theory]
    // Step 1 suffixes (e/em/en/ern/er/es/s) + R1.
    [InlineData("rechnungen", "rechnung")]
    [InlineData("rechnung", "rechnung")]
    [InlineData("aufeinander", "aufeinand")]
    [InlineData("kinder", "kind")]
    [InlineData("kindes", "kind")]
    [InlineData("kinds", "kind")]
    [InlineData("katzen", "katz")]
    // Umlaut removal in the final step.
    [InlineData("häuser", "haus")]
    [InlineData("haus", "haus")]
    [InlineData("gläser", "glas")]
    // The niss rule of step 1.
    [InlineData("bedürfnisse", "bedurfnis")]
    [InlineData("bedürfnis", "bedurfnis")]
    // Step 2, including the st rule (valid st-ending preceded by at least 3 letters).
    [InlineData("längst", "lang")]
    [InlineData("länger", "lang")]
    [InlineData("lang", "lang")]
    // Step 3 d-suffixes (ung/lich/keit/ig) with their R2 conditions.
    [InlineData("verbindungen", "verbind")]
    [InlineData("verbindung", "verbind")]
    [InlineData("verständlich", "verstand")]
    [InlineData("verantwortlichkeit", "verantwort")]
    [InlineData("auffällig", "auffall")]
    [InlineData("wenige", "wenig")]
    // U-trick: u between vowels is treated as a consonant, so R1 allows the deletion.
    [InlineData("bauen", "bau")]
    [InlineData("abenteuer", "abenteu")]
    public void Stem_MatchesOfficialSnowballOutput(string token, string expected)
        => Assert.Equal(expected, Stemmer.Stem(token));

    [Theory]
    [InlineData("rechnungen", "rechnung")]
    [InlineData("häuser", "haus")]
    [InlineData("verbindungen", "verbindung")]
    [InlineData("möglichkeiten", "möglichkeit")]
    [InlineData("bedürfnisse", "bedürfnis")]
    [InlineData("reinigungen", "reinigung")]
    [InlineData("länger", "längst")]
    [InlineData("katzen", "katze")]
    [InlineData("arbeiten", "arbeit")]
    public void Stem_InflectionsOfOneLemma_ProduceTheSameStem(string first, string second)
        => Assert.Equal(Stemmer.Stem(first), Stemmer.Stem(second));

    [Theory]
    [InlineData("rechnungen")]
    [InlineData("häuser")]
    [InlineData("verbindungen")]
    [InlineData("möglichkeiten")]
    [InlineData("aufeinander")]
    [InlineData("bauen")]
    public void Stem_IsIdempotent(string token)
    {
        var once = Stemmer.Stem(token);

        Assert.Equal(once, Stemmer.Stem(once));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("es")]
    [InlineData("zu")]
    public void Stem_EmptyAndShortTokens_AreReturnedUnchanged(string token)
        => Assert.Equal(token, Stemmer.Stem(token));
}
