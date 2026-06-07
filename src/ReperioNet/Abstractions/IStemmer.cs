namespace ReperioNet.Abstractions;

/// <summary>Reduces a token to its stem (e.g. <c>"running"</c> → <c>"run"</c>).</summary>
/// <remarks>Implementations must be thread-safe: ReperioNet calls <see cref="Stem"/> concurrently from parallel searches and bulk indexing.</remarks>
public interface IStemmer
{
    /// <summary>Returns the stem of <paramref name="token"/>.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    string Stem(string token);
}
