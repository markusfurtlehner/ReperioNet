namespace ReperioNet.Abstractions;

/// <summary>Encodes a token into a phonetic key (e.g. Kölner Phonetik, Double Metaphone).</summary>
public interface IPhoneticEncoder
{
    /// <summary>Returns the phonetic code for <paramref name="token"/>, or <see langword="null"/> if the token is not encodable.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    string? Encode(string token);
}
