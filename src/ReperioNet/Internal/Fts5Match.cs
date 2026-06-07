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

    /// <summary>Builds the Milestone-2 base-only match expression: <c>base : ("t1" OR "t2" OR ...)</c>.</summary>
    internal static string BuildBaseMatch(IReadOnlyList<string> tokens)
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

        return builder.Append(')').ToString();
    }
}
