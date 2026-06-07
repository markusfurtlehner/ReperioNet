using FuzzySharp;
using ReperioNet.Abstractions;

namespace ReperioNet;

/// <summary>
/// Default <see cref="IFuzzyRanker"/>: FuzzySharp's token-set ratio scaled to 0..1.
/// Handles word-order differences and partial token overlap well for typo-tolerant document search.
/// </summary>
public sealed class TokenSetFuzzyRanker : IFuzzyRanker
{
    /// <inheritdoc />
    public double Score(string query, string candidateText)
        => Fuzz.TokenSetRatio(query, candidateText) / 100.0;
}
