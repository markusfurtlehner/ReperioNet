using ReperioNet.Internal;
using Xunit;

namespace ReperioNet.Tests;

public class Fts5MatchTests
{
    [Fact]
    public void EscapeToken_WrapsInQuotes()
    {
        Assert.Equal("\"rechnung\"", Fts5Match.EscapeToken("rechnung"));
    }

    [Fact]
    public void EscapeToken_DoublesEmbeddedQuotes()
    {
        Assert.Equal("\"ab\"\"c\"", Fts5Match.EscapeToken("ab\"c"));
    }

    [Fact]
    public void BuildBaseMatch_SingleToken()
    {
        Assert.Equal("base : (\"foo\")", Fts5Match.BuildBaseMatch(["foo"]));
    }

    [Fact]
    public void BuildBaseMatch_MultipleTokens_OrCombined()
    {
        Assert.Equal("base : (\"foo\" OR \"bar\" OR \"42\")", Fts5Match.BuildBaseMatch(["foo", "bar", "42"]));
    }

    [Fact]
    public void BuildBaseMatch_PrefixAid_AppendsPrefixTermOnLastToken()
    {
        Assert.Equal("base : (\"ab\" OR \"ab\"*)", Fts5Match.BuildBaseMatch(["ab"], prefixLastToken: true));
        Assert.Equal(
            "base : (\"a\" OR \"b\" OR \"b\"*)",
            Fts5Match.BuildBaseMatch(["a", "b"], prefixLastToken: true));
    }
}
