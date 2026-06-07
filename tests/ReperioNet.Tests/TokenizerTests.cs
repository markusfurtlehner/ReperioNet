using ReperioNet.Internal;
using Xunit;

namespace ReperioNet.Tests;

public class TokenizerTests
{
    [Fact]
    public void Tokenize_SplitsOnNonLetterOrDigitRunes()
    {
        Assert.Equal(
            ["hello", "world", "foo", "bar"],
            Tokenizer.Tokenize("Hello, world-foo_bar"));
    }

    [Fact]
    public void Tokenize_LowercasesInvariant()
    {
        Assert.Equal(["hello", "world"], Tokenizer.Tokenize("HeLLo WORLD"));
    }

    [Fact]
    public void Tokenize_PreservesDiacritics()
    {
        // §15.3 (binding): diacritics are NOT stripped in C# — FTS5 folds them on both sides.
        Assert.Equal(["müller", "straße"], Tokenizer.Tokenize("Müller Straße"));
    }

    [Fact]
    public void Tokenize_KeepsDigitsAndAlphanumerics()
    {
        Assert.Equal(["abc123", "42"], Tokenizer.Tokenize("abc123 42"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!?!---...")]
    public void Tokenize_NoLetterOrDigit_ReturnsEmpty(string text)
    {
        Assert.Empty(Tokenizer.Tokenize(text));
    }

    [Fact]
    public void Tokenize_SplitsOnEmojiAndSymbols()
    {
        Assert.Equal(["a", "b"], Tokenizer.Tokenize("a😀b"));
    }

    [Fact]
    public void Tokenize_PreservesTokenOrder()
    {
        Assert.Equal(["one", "two", "three"], Tokenizer.Tokenize("one two three"));
    }

    [Fact]
    public void Tokenize_HandlesCyrillic()
    {
        Assert.Equal(["счёт", "оплачен"], Tokenizer.Tokenize("Счёт оплачен!"));
    }
}
