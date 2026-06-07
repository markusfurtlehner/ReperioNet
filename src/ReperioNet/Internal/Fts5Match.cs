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
    {
        var builder = new StringBuilder("base : (");
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" OR ");
            }

            builder.Append(EscapeToken(tokens[i]));
        }

        if (prefixLastToken && tokens.Count > 0)
        {
            builder.Append(" OR ").Append(EscapeToken(tokens[^1])).Append('*');
        }

        return builder.Append(')').ToString();
    }
}
