namespace ReperioNet.Abstractions;

/// <summary>Detects the language of a piece of text.</summary>
public interface ILanguageDetector
{
    /// <summary>Returns the ISO 639-1 code of the detected language, or <see langword="null"/> if detection is uncertain.</summary>
    /// <param name="text">The text to analyze.</param>
    string? Detect(string text);
}
