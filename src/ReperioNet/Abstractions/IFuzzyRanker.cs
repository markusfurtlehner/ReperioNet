namespace ReperioNet.Abstractions;

/// <summary>Scores the fuzzy similarity between a query and a candidate text.</summary>
public interface IFuzzyRanker
{
    /// <summary>Returns a similarity score in the range 0..1 (higher = more similar).</summary>
    /// <param name="query">The raw user query.</param>
    /// <param name="candidateText">The candidate document text to compare against.</param>
    double Score(string query, string candidateText);
}
