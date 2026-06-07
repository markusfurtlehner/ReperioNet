namespace ReperioNet;

/// <summary>How multiple query terms are combined on the <c>base</c> column (PRD §9.5).</summary>
public enum TermMatch
{
    /// <summary>
    /// Every term must occur in the document (implicit FTS5 AND). When this strict pass yields
    /// fewer candidates than <see cref="SearchQueryOptions.Limit"/>, an <see cref="AnyTerms"/>
    /// pass widens recall automatically; all-terms matches always rank ahead of fallback matches.
    /// The intersection is small for selective terms, which makes this both the more common user
    /// intent and far cheaper to rank than OR. Single-token queries are unaffected.
    /// </summary>
    AllTerms,

    /// <summary>Any term may occur (OR-combined base clause) — the widest recall.</summary>
    AnyTerms,
}
