using FuzzySharp;
using Xunit;

namespace ReperioNet.Tests;

/// <summary>
/// 2.6.2: <see cref="TokenSetFuzzyRanker"/> answers without computing the token-set ratio when the text holds every token of the
/// query (1.0) and when a long text holds none (0.0). The answer must be the one the ratio gives.
/// </summary>
public class TokenSetShortcutTests
{
    private static readonly string[] Words =
    [
        "und", "Und", "UND", "rechnung", "Rechnung", "für", "Für", "über", "straße", "bund", "bundes", "a", "I", "42", "2026", "x1",
        "re", "mail", "e-mail", "o'brien", "naïve", "日本", "привет", "mueller", "müller", "the", "The", "in", "INBOX", "",
        "und,", "(und)", "kunde", "found", "under",
    ];

    // White space of every kind the ratio splits at, characters it does not split at (U+001C, punctuation), and none at all.
    private static readonly string[] Separators =
    [
        " ", "  ", C(10), C(9), ", ", ".", "-", "_", "/", ": ", C(13) + C(10), "; ", "!", " (", ") ", C(0xA0), C(0x85), C(0x2028), C(0x3000),
        C(0x1C), C(11), C(12), " ", " ", " ",
    ];

    private static string C(int code) => ((char)code).ToString();

    [Fact]
    public void TheShortcuts_GiveWhatTheTokenSetRatioGives_ForShortTexts()
    {
        var random = new Random(20261114);
        var ranker = new TokenSetFuzzyRanker();
        var every = 0;
        for (var round = 0; round < 20_000; round++)
        {
            var query = Text(random, random.Next(0, 4), Words);
            var text = Text(random, random.Next(0, 12), Words);
            var expected = Fuzz.TokenSetRatio(query, text) / 100.0;

            if (TokenSetFuzzyRanker.Known(query, text) == TokenSetFuzzyRanker.Shortcut.EveryToken)
            {
                every++;
            }

            Assert.True(expected == ranker.Score(query, text), $"query '{query}', text '{text}': ratio {expected}, ranker {ranker.Score(query, text)}");
        }

        // The test means nothing if the shortcut is never taken.
        Assert.InRange(every, 50, 20_000);
    }

    [Fact]
    public void TheShortcuts_GiveWhatTheTokenSetRatioGives_ForLongTexts()
    {
        var random = new Random(7);
        var ranker = new TokenSetFuzzyRanker();
        // A vocabulary of made-up words, so that a long text often holds no token of the query.
        var vocabulary = Enumerable.Range(0, 3_000)
            .Select(_ => new string(Enumerable.Range(0, random.Next(1, 12)).Select(_ => (char)('a' + random.Next(26))).ToArray()))
            .Concat(Words)
            .ToArray();
        var none = 0;
        var every = 0;
        for (var round = 0; round < 1_500; round++)
        {
            var query = Text(random, random.Next(1, 3), round % 3 == 0 ? Words : vocabulary).Trim();
            var text = Text(random, random.Next(50, 1_500), round % 5 == 0 ? Words : vocabulary);
            var expected = Fuzz.TokenSetRatio(query, text) / 100.0;

            switch (TokenSetFuzzyRanker.Known(query, text))
            {
                case TokenSetFuzzyRanker.Shortcut.NoTokenInALongText: none++; break;
                case TokenSetFuzzyRanker.Shortcut.EveryToken: every++; break;
            }

            Assert.True(expected == ranker.Score(query, text), $"query '{query}', text of {text.Length} characters: ratio {expected}, ranker {ranker.Score(query, text)}");
        }

        Assert.InRange(none, 100, 1_500);
        Assert.InRange(every, 20, 1_500);
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

    [Fact]
    public void ALongTextWithoutAnyTokenOfTheQuery_IsKnownToScoreZero_AShortOneIsNot()
    {
        var words = string.Join(' ', Enumerable.Range(0, 2_000).Select(i => "wort" + i));

        Assert.Equal(TokenSetFuzzyRanker.Shortcut.NoTokenInALongText, TokenSetFuzzyRanker.Known("und", words));
        Assert.Equal(0.0, new TokenSetFuzzyRanker().Score("und", words));
        Assert.Equal(TokenSetFuzzyRanker.Shortcut.None, TokenSetFuzzyRanker.Known("und", "Kunde gefunden"));
        // The same few words many times are a short text for the ratio: it compares distinct tokens.
        Assert.Equal(TokenSetFuzzyRanker.Shortcut.None, TokenSetFuzzyRanker.Known("und", string.Join(' ', Enumerable.Repeat("Kunde gefunden", 2_000))));
        // One token of two found: the ratio must be computed.
        Assert.Equal(TokenSetFuzzyRanker.Shortcut.None, TokenSetFuzzyRanker.Known("und wort7", words));
    }

    private static string Text(Random random, int words, string[] from)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < words; i++)
        {
            text.Append(from[random.Next(from.Length)]).Append(Separators[random.Next(Separators.Length)]);
        }

        return text.ToString();
    }
}
