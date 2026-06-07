namespace ReperioNet;

/// <summary>
/// Named index-layout presets derived from the benchmark matrix (see <c>benchmarks/RESULTS.md</c>).
/// The layout flags they set are persisted in the index: reopening an existing database with a
/// different profile throws <see cref="ReperioException"/> — open with the original options and
/// call <c>RebuildAsync()</c> after changing the flags, or start a new database file.
/// </summary>
public static class ReperioProfiles
{
    /// <summary>
    /// Full-fidelity layout for desktop/server use — this preset equals the option defaults and
    /// exists to make the choice explicit and chainable.
    /// Sets <see cref="ReperioOptions{TMeta}.EnableTrigram"/> = true,
    /// <see cref="ReperioOptions{TMeta}.StoreContent"/> = true,
    /// <see cref="ReperioOptions{TMeta}.EnablePhonetic"/> = true,
    /// <see cref="ReperioOptions{TMeta}.RemoveStopWords"/> = false,
    /// <see cref="ReperioOptions{TMeta}.MaxContentChars"/> = 0 (unbounded).
    /// </summary>
    /// <remarks>
    /// Best recall: mid-word substring search via the trigram index, snippets, phonetic variants,
    /// stop words kept in every stream. The cost is database size (~4.4x the raw content in the
    /// benchmark corpus — the trigram index alone is roughly half the database) and the slowest
    /// indexing throughput of the profiles.
    /// </remarks>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options to configure.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> UseDesktopProfile<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.EnableTrigram = true;
        o.StoreContent = true;
        o.EnablePhonetic = true;
        o.RemoveStopWords = false;
        o.MaxContentChars = 0;
        return o;
    }

    /// <summary>
    /// Size- and battery-conscious layout for phones/tablets.
    /// Sets <see cref="ReperioOptions{TMeta}.EnableTrigram"/> = false,
    /// <see cref="ReperioOptions{TMeta}.StoreContent"/> = true,
    /// <see cref="ReperioOptions{TMeta}.EnablePhonetic"/> = true,
    /// <see cref="ReperioOptions{TMeta}.RemoveStopWords"/> = true,
    /// <see cref="ReperioOptions{TMeta}.MaxContentChars"/> = 4000.
    /// </summary>
    /// <remarks>
    /// <para>Rationale (benchmark-derived): dropping the trigram index is the one change that
    /// improves database size (~4.4x → ~2x raw content), query latency and indexing throughput
    /// together — the only loss is mid-word substring search. <see cref="ReperioOptions{TMeta}.StoreContent"/>
    /// stays on because it is free with respect to size — when content is not stored, the §15.4
    /// layout keeps the same text in <c>rank_text</c> for fuzzy re-ranking, so turning it off saves
    /// nothing and only costs snippets. Phonetic codes stay on (cheap, valuable for name/spelling
    /// variants). Removing stop words from the stem/phonetic streams trims the common-term match
    /// cost. <see cref="ReperioOptions{TMeta}.MaxContentChars"/> is the only lever that shrinks the
    /// database below the rank_text floor for long bodies; 4000 characters is a starting default —
    /// tune it to your content.</para>
    /// <para>What is kept despite the smaller index: typo tolerance (fuzzy re-ranking over
    /// content/rank_text), word forms (stemming), phonetic variants, prefix matching for short
    /// queries, and snippets. What is lost: mid-word substring search (trigram).</para>
    /// </remarks>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options to configure.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> UseMobileProfile<TMeta>(this ReperioOptions<TMeta> o)
    {
        o.EnableTrigram = false;
        o.StoreContent = true;
        o.EnablePhonetic = true;
        o.RemoveStopWords = true;
        o.MaxContentChars = 4000;
        return o;
    }
}
