using System.Text;

namespace ReperioNet.Internal;

/// <summary>
/// Builds highlighted snippets per PRD §9.13: a window of up to <see cref="SnippetOptions.MaxLength"/>
/// characters of the stored content, centered on the first (diacritic/case-insensitive) occurrence of
/// any base query token, with every matched token occurrence wrapped in the configured markers. If no
/// token occurs in the content, the first MaxLength characters are returned without markers.
/// </summary>
internal static class SnippetBuilder
{
    internal static string Build(string content, IReadOnlyList<string> queryTokens, SnippetOptions options)
    {
        if (string.IsNullOrEmpty(content) || options.MaxLength <= 0)
        {
            return string.Empty;
        }

        var (folded, origStart, origEnd) = TextFold.FoldWithMap(content);

        var foldedTokens = new List<string>();
        foreach (var token in queryTokens)
        {
            var foldedToken = TextFold.Fold(token);
            if (foldedToken.Length > 0 && !foldedTokens.Contains(foldedToken))
            {
                foldedTokens.Add(foldedToken);
            }
        }

        // First occurrence (diacritic/case-insensitive, substring semantics) of any base token.
        var firstIndex = -1;
        var firstLength = 0;
        foreach (var token in foldedTokens)
        {
            var index = folded.IndexOf(token, StringComparison.Ordinal);
            if (index >= 0 && (firstIndex < 0 || index < firstIndex))
            {
                firstIndex = index;
                firstLength = token.Length;
            }
        }

        if (firstIndex < 0)
        {
            // No token found: the first MaxLength characters, no markers.
            return content.Length <= options.MaxLength ? content : CutSurrogateSafe(content, options.MaxLength);
        }

        // Window of up to MaxLength original characters centered on the first occurrence.
        var windowLength = Math.Min(options.MaxLength, content.Length);
        var matchStart = origStart[firstIndex];
        var matchEnd = origEnd[firstIndex + firstLength - 1];
        var start = ((matchStart + matchEnd) / 2) - (windowLength / 2);
        start = Math.Clamp(start, 0, content.Length - windowLength);
        var end = start + windowLength;

        // Never split surrogate pairs at the window edges (shrink inward).
        if (start > 0 && char.IsLowSurrogate(content[start]))
        {
            start++;
        }

        if (end < content.Length && char.IsLowSurrogate(content[end]))
        {
            end--;
        }

        // Every token occurrence intersecting the window, clamped to it and merged where
        // overlapping or adjacent, so markers never nest.
        var spans = new List<(int Start, int End)>();
        foreach (var token in foldedTokens)
        {
            var from = 0;
            int index;
            while ((index = folded.IndexOf(token, from, StringComparison.Ordinal)) >= 0)
            {
                var spanStart = origStart[index];
                var spanEnd = origEnd[index + token.Length - 1];
                if (spanStart < end && spanEnd > start)
                {
                    spans.Add((Math.Max(spanStart, start), Math.Min(spanEnd, end)));
                }

                from = index + 1;
            }
        }

        spans.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
        var merged = new List<(int Start, int End)>();
        foreach (var span in spans)
        {
            if (merged.Count > 0 && span.Start <= merged[^1].End)
            {
                if (span.End > merged[^1].End)
                {
                    merged[^1] = (merged[^1].Start, span.End);
                }
            }
            else
            {
                merged.Add(span);
            }
        }

        var builder = new StringBuilder();
        var cursor = start;
        foreach (var (spanStart, spanEnd) in merged)
        {
            builder
                .Append(content, cursor, spanStart - cursor)
                .Append(options.StartMarker)
                .Append(content, spanStart, spanEnd - spanStart)
                .Append(options.EndMarker);
            cursor = spanEnd;
        }

        return builder.Append(content, cursor, end - cursor).ToString();
    }

    private static string CutSurrogateSafe(string content, int length)
    {
        if (length < content.Length && char.IsLowSurrogate(content[length]))
        {
            length--;
        }

        return content[..length];
    }
}
