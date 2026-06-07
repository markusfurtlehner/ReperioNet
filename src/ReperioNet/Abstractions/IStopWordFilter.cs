namespace ReperioNet.Abstractions;

/// <summary>Identifies stop words for a language (used only when <c>ReperioOptions&lt;TMeta&gt;.RemoveStopWords</c> is enabled).</summary>
/// <remarks>Implementations must be thread-safe: ReperioNet calls <see cref="IsStopWord"/> concurrently from parallel searches and bulk indexing.</remarks>
public interface IStopWordFilter
{
    /// <summary>Returns <see langword="true"/> if <paramref name="token"/> is a stop word.</summary>
    /// <param name="token">A single normalized (lowercased) token.</param>
    bool IsStopWord(string token);
}
