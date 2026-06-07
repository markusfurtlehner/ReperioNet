using ReperioNet.Internal;
using Xunit;

namespace ReperioNet.Tests;

/// <summary>Unit tests for the §9.13 snippet algorithm, including branches integration can't reach.</summary>
public class SnippetBuilderTests
{
    private static SnippetOptions Options(int maxLength = 200, string start = "<mark>", string end = "</mark>")
        => new() { MaxLength = maxLength, StartMarker = start, EndMarker = end };

    [Fact]
    public void NoTokenFound_ReturnsFirstMaxLengthCharsWithoutMarkers()
    {
        var snippet = SnippetBuilder.Build("hello world example text", ["zzz"], Options(maxLength: 11));

        Assert.Equal("hello world", snippet);
    }

    [Fact]
    public void NoTokenFound_ShortContent_ReturnsWholeContent()
    {
        var snippet = SnippetBuilder.Build("short text", ["zzz"], Options());

        Assert.Equal("short text", snippet);
    }

    [Fact]
    public void EmptyTokenList_FallsBackToContentStart()
    {
        var snippet = SnippetBuilder.Build("hello world", [], Options(maxLength: 5));

        Assert.Equal("hello", snippet);
    }

    [Fact]
    public void OverlappingTokenOccurrences_MergeIntoOneMark()
    {
        var snippet = SnippetBuilder.Build("rechnung", ["rech", "rechnung"], Options());

        Assert.Equal("<mark>rechnung</mark>", snippet);
    }

    [Fact]
    public void AdjacentTokenOccurrences_MergeIntoOneMark()
    {
        var snippet = SnippetBuilder.Build("rechnung", ["rech", "nung"], Options());

        Assert.Equal("<mark>rechnung</mark>", snippet);
    }

    [Fact]
    public void MultipleTokens_EachOccurrenceMarked()
    {
        var snippet = SnippetBuilder.Build("alpha und beta", ["alpha", "beta"], Options());

        Assert.Equal("<mark>alpha</mark> und <mark>beta</mark>", snippet);
    }

    [Fact]
    public void MatchNearStart_WindowClampsToContentStart()
    {
        var snippet = SnippetBuilder.Build("kafka " + new string('b', 100), ["kafka"], Options(maxLength: 11));

        Assert.Equal("<mark>kafka</mark> bbbbb", snippet);
    }

    [Fact]
    public void MatchNearEnd_WindowClampsToContentEnd()
    {
        var snippet = SnippetBuilder.Build(new string('b', 100) + " kafka", ["kafka"], Options(maxLength: 11));

        Assert.Equal("bbbbb <mark>kafka</mark>", snippet);
    }

    [Fact]
    public void DiacriticFolding_MapsBackToOriginalSpans()
    {
        var snippet = SnippetBuilder.Build("Crème Brûlée bestellt", ["creme", "brulee"], Options());

        Assert.Equal("<mark>Crème</mark> <mark>Brûlée</mark> bestellt", snippet);
    }

    [Fact]
    public void EmptyContent_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SnippetBuilder.Build("", ["x"], Options()));
    }

    [Fact]
    public void WindowShorterThanMatch_StillProducesClampedMark()
    {
        // Window (4 chars) smaller than the matched token (8 chars): the mark is clamped to the window.
        var snippet = SnippetBuilder.Build("rechnung", ["rechnung"], Options(maxLength: 4));

        Assert.Equal("<mark>chnu</mark>", snippet);
    }
}
