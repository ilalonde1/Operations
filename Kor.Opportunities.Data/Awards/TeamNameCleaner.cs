#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// Shared cleaner for project-team organisation names (architect / structural /
/// GC / proponent) applied BEFORE a name reaches <see cref="CanonicalOrgResolver"/>.
///
/// Promoted out of <c>tools/BdResearchImport</c> so that every front door — the
/// MPI ingest providers, the research importer, the queue-drain ingester —
/// applies the same guard, rather than only the one CLI. Without it the
/// scheduled providers feed raw multi-firm narrative strings ("Firm A (architect
/// of record); Firm B", "Not publicly confirmed") straight to the resolver — the
/// migration-153 junk-org class that then has to be cleaned up by hand.
///
/// Returns <c>null</c> when the input is blank, an uncertainty phrase, or cannot
/// be reduced to a single plausible firm; otherwise returns the single lead firm
/// with trailing role annotations removed. Ported verbatim from the CLI's proven
/// <c>CleanTeamName</c> so behaviour is identical across every caller.
/// </summary>
public static class TeamNameCleaner
{
    private static readonly Regex TrailingParenthetical = new(@"\s*\([^()]*\)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Reduce a raw team-name string to a single plausible firm, or null.
    /// </summary>
    public static string? Clean(string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
        if (trimmed is null || string.Equals(trimmed, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Uncertainty phrasing means no single firm can honestly hold the seat.
        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("neither") || lower.Contains("not publicly") || lower.Contains("not confirmed")
            || lower.Contains("not disclosed") || lower.Contains("in-house within") || lower.Contains(" tbd"))
        {
            return null;
        }

        if (trimmed.Contains(';') || trimmed.Contains(" / "))
        {
            // Split only at depth 0 — separators inside parentheses are annotation
            // text, not firm boundaries ("A Inc. (with B / C as architect of record)").
            var segments = SplitSegments(trimmed);
            if (segments.Length == 0) return null;

            // A segment annotated as the firm of record / prime wins; else the first.
            static bool IsOfRecord(string s)
            {
                var l = s.ToLowerInvariant();
                return l.Contains("record") || l.Contains("prime") || l.Contains("confirmed");
            }

            trimmed = (segments.FirstOrDefault(IsOfRecord) ?? segments[0]).Trim();
        }

        // Strip trailing role annotations: "Kasian (architect of record)" -> "Kasian".
        while (TrailingParenthetical.IsMatch(trimmed))
        {
            trimmed = TrailingParenthetical.Replace(trimmed, string.Empty).Trim();
        }

        // Whatever survives must look like a name, not a sentence fragment.
        if (trimmed.Length == 0 || trimmed.Contains(';')
            || trimmed.Contains('(') || trimmed.Contains(')'))
        {
            return null;
        }

        var lowered = trimmed.ToLowerInvariant();
        if (lowered.Contains("multiple") || lowered.Contains("various") || lowered.Contains("unnamed")
            || lowered == "tbd" || lowered.StartsWith("unknown"))
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Split a team string into firm segments at depth-0 <c>;</c> or <c> / </c>
    /// separators, ignoring separators nested inside parentheses.
    /// </summary>
    public static string[] SplitSegments(string value)
    {
        var segments = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);

            if (depth == 0 && c == ';')
            {
                segments.Add(current.ToString());
                current.Clear();
                continue;
            }

            if (depth == 0 && c == '/'
                && i > 0 && char.IsWhiteSpace(value[i - 1])
                && i + 1 < value.Length && char.IsWhiteSpace(value[i + 1]))
            {
                segments.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        segments.Add(current.ToString());
        return segments.Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
    }
}
