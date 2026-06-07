namespace ReperioNet.Abstractions;

/// <summary>Reduces a token to its stem (e.g. <c>"running"</c> → <c>"run"</c>).</summary>
public interface IStemmer
{
    /// <summary>Returns the stem of <paramref name="token"/>.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    string Stem(string token);
}
