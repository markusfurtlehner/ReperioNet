using ReperioNet.LanguageDetection;
using Xunit;

namespace ReperioNet.Languages.Tests;

public class NTextCatDetectorTests
{
    // One detector for the whole class: profile loading is the expensive part.
    private static readonly NTextCatDetector Detector = new();

    [Theory]
    [InlineData("Die Rechnungen sind gestern angekommen und wurden von der Buchhaltung sorgfältig geprüft", "de")]
    [InlineData("The invoices were received yesterday and have been carefully checked by the accounting team", "en")]
    [InlineData("Les chevaux galopent rapidement à travers la campagne française pendant l'été", "fr")]
    [InlineData("Los gatos duermen tranquilamente en el jardín durante toda la tarde", "es")]
    [InlineData("I ragazzi giocano a calcio nel parco vicino alla scuola ogni pomeriggio", "it")]
    [InlineData("Os meninos estão brincando no parque perto da escola durante a tarde", "pt")]
    [InlineData("De kinderen spelen elke middag in het park naast de school met hun vrienden", "nl")]
    [InlineData("Barnen leker i parken bredvid skolan varje eftermiddag tillsammans med sina vänner", "sv")]
    [InlineData("Книги лежат на столе в библиотеке рядом с большим окном", "ru")]
    public void Detect_RecognizesLanguages(string text, string expected)
    {
        Assert.Equal(expected, Detector.Detect(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Detect_BlankText_ReturnsNull(string text)
    {
        Assert.Null(Detector.Detect(text));
    }

    [Fact]
    public void Detect_IsSafeForConcurrentUse()
    {
        var texts = new[]
        {
            "Die Rechnungen sind gestern angekommen und wurden sorgfältig geprüft",
            "The invoices were received yesterday and have been carefully checked",
            "Les chevaux galopent rapidement à travers la campagne française",
        };

        var results = texts
            .SelectMany(text => Enumerable.Repeat(text, 20))
            .AsParallel()
            .Select(Detector.Detect)
            .ToList();

        Assert.All(results, code => Assert.Contains(code, new[] { "de", "en", "fr" }));
    }

    [Fact]
    public void Constructor_MissingProfile_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new NTextCatDetector("/nonexistent/profile.xml"));
    }
}
