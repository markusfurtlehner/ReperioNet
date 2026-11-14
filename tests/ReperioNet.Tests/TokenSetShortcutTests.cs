using FuzzySharp;
using Xunit;

namespace ReperioNet.Tests;

/// <summary>
/// 2.6.2: <see cref="TokenSetFuzzyRanker"/> answers 1.0 without the token-set ratio when the text holds every token of the query.
/// The answer must be the one the ratio gives.
/// </summary>
public class TokenSetShortcutTests
{
    private static readonly string[] Words =
    [
        "und", "Und", "UND", "rechnung", "Rechnung", "für", "Für", "über", "straße", "bund", "bundes", "a", "I", "42", "2026", "x1",
        "re", "mail", "e-mail", "o'brien", "naïve", "日本", "привет", "mueller", "müller", "the", "The", "in", "INBOX", "",
    ];

    private static readonly string[] Separators = [" ", "  ", "\n", "\t", ", ", ".", "-", "_", "/", ": ", "\r\n", "; ", "!", " (", ") "];

    [Fact]
    public void TheShortcut_GivesWhatTheTokenSetRatioGives()
    {
        var random = new Random(20261114);
        var ranker = new TokenSetFuzzyRanker();
        var shortcuts = 0;
        for (var round = 0; round < 20_000; round++)
        {
            var query = Text(random, random.Next(0, 4));
            var text = Text(random, random.Next(0, 12));
            var expected = Fuzz.TokenSetRatio(query, text) / 100.0;

            if (TokenSetFuzzyRanker.ContainsEveryToken(query, text))
            {
                shortcuts++;
            }

            Assert.True(expected == ranker.Score(query, text), $"query '{query}', text '{text}': ratio {expected}, ranker {ranker.Score(query, text)}");
        }

        // The test means nothing if the shortcut is never taken.
        Assert.InRange(shortcuts, 50, 20_000);
    }

    [Theory]
    [InlineData("und", "Hund und Katze", true)]
    [InlineData("und", "Bundesrechnung", false)]
    [InlineData("und", "Hund und, Katze", false)]
    [InlineData("UND", "das und das", false)]
    [InlineData("Rechnung Amazon", "Ihre Amazon Rechnung", true)]
    [InlineData("Rechnung Amazon", "Ihre Rechnung", false)]
    [InlineData("Rechnung Rechnung", "eine Rechnung", true)]
    [InlineData("", "text", false)]
    [InlineData("   ", "text", false)]
    [InlineData("text", "", false)]
    public void ContainsEveryToken_ComparesWholeTokensExactly(string query, string text, bool expected)
        => Assert.Equal(expected, TokenSetFuzzyRanker.ContainsEveryToken(query, text));

    private static string Text(Random random, int words)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < words; i++)
        {
            text.Append(Words[random.Next(Words.Length)]).Append(Separators[random.Next(Separators.Length)]);
        }

        return text.ToString();
    }
}
