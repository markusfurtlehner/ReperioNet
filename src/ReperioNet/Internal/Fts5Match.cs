using System.Text;

namespace ReperioNet.Internal;

/// <summary>
/// Builds FTS5 MATCH expressions from escaped query tokens (PRD §9.4, §15.6). MATCH expressions are
/// built ONLY from escaped tokens — raw input is never interpolated.
/// </summary>
internal static class Fts5Match
{
    /// <summary>Escapes a token for FTS5: doubles embedded quotes and wraps the token in double quotes.</summary>
    internal static string EscapeToken(string token)
        => "\"" + token.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Builds the base-column match expression: <c>base : ("t1" OR "t2" OR ...)</c>. With
    /// <paramref name="prefixLastToken"/> (the §9.5 short-query aid for queries shorter than three
    /// characters), an FTS5 prefix term on the last token is OR-appended: <c>OR "tN"*</c>.
    /// </summary>
    internal static string BuildBaseMatch(IReadOnlyList<string> tokens, bool prefixLastToken = false)
        => BuildMatch(tokens, prefixLastToken, stemTokens: null, phoneticTokens: null);

    /// <summary>
    /// Builds the full §9.5 match expression, OR-combining the column-scoped clauses that are
    /// non-empty: <c>base : (...) OR stem : (...) OR phonetic : (...)</c>.
    /// </summary>
    internal static string BuildMatch(
        IReadOnlyList<string> baseTokens,
        bool prefixLastToken,
        IReadOnlyList<string>? stemTokens,
        IReadOnlyList<string>? phoneticTokens)
    {
        var builder = new StringBuilder();
        AppendColumnClause(builder, "base", baseTokens, prefixLastToken);

        if (stemTokens is { Count: > 0 })
        {
            builder.Append(" OR ");
            AppendColumnClause(builder, "stem", stemTokens, prefixLast: false);
        }

        if (phoneticTokens is { Count: > 0 })
        {
            builder.Append(" OR ");
            AppendColumnClause(builder, "phonetic", phoneticTokens, prefixLast: false);
        }

        return builder.ToString();
    }

    private static void AppendColumnClause(StringBuilder builder, string column, IReadOnlyList<string> tokens, bool prefixLast)
    {
        builder.Append(column).Append(" : (");
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" OR ");
            }

            builder.Append(EscapeToken(tokens[i]));
        }

        if (prefixLast && tokens.Count > 0)
        {
            builder.Append(" OR ").Append(EscapeToken(tokens[^1])).Append('*');
        }

        builder.Append(')');
    }
}
