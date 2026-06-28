#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Layer 1 (exact, NO AI): the canonical sheet identity an estimator reads off the title block —
    /// which physical floor this plan draws (<see cref="Level"/>) and, for match-line sets, which half
    /// (<see cref="Zone"/>, e.g. NORTH/SOUTH). This is what lets the takeoff count one slab per
    /// (level, zone): NORTH + SOUTH of a floor are distinct plates that SUM, but the same (level, zone)
    /// drawn on two sheets (framing plan vs reinforcing plan) is ONE slab — counting both double-counts.
    /// Read deterministically from the vector title text, so it carries none of the free-text variance
    /// of a synthesised level name.
    /// </summary>
    public sealed record SheetTitle(string Level, string? Zone, string Raw)
    {
        /// <summary>Stable key for de-duplicating slab plates: "P4|NORTH" / "L12|" (no zone).</summary>
        public string Key => $"{Level}|{Zone}";

        /// <summary>Human label for the level row: "P4 NORTH" / "L12".</summary>
        public string Display => Zone is null ? Level : $"{Level} {Zone}";
    }

    public static class SheetTitleReader
    {
        // "LEVEL P4 PLAN NORTH", "LEVEL 12 FLOOR PLAN", "ROOF PLAN SOUTH" — anchor on PLAN, take the
        // level token before it and an optional cardinal half after it. Tolerant of the title-block text
        // colliding onto a note's baseline (as it does on these dense CAD sheets).
        private static readonly Regex TitleRx = new(
            @"(?:LEVEL\s+)?(?<lvl>P\d{1,2}|L\d{1,2}|PH\d?|ROOF|MAIN|GROUND|\d{1,2})\s+(?:FLOOR\s+|FRAMING\s+|FORMWORK\s+)?PLAN(?:\s+(?<zone>NORTH|SOUTH|EAST|WEST))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Extract the dominant sheet title from a page's text lines, or null if none reads as a plan.
        /// Cross-references ("SEE LEVEL P4 PLAN…") and the real title both match the pattern, so the
        /// dominant (level, zone) — the one that actually appears most — wins; ties prefer a zoned title.
        /// </summary>
        public static SheetTitle? FromLines(IReadOnlyList<string>? lines)
        {
            if (lines is null) return null;

            var hits = new List<(string Lvl, string? Zone, string Raw)>();
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                foreach (Match m in TitleRx.Matches(line))
                {
                    string lvl = m.Groups["lvl"].Value.ToUpperInvariant();
                    string? zone = m.Groups["zone"].Success ? m.Groups["zone"].Value.ToUpperInvariant() : null;
                    hits.Add((lvl, zone, m.Value.Trim()));
                }
            }
            if (hits.Count == 0) return null;

            // Dominant (level, zone): most frequent; break ties toward a zoned title (a real match-line
            // sheet title), then deterministically by key so the result never depends on scan order.
            var best = hits
                .GroupBy(h => (h.Lvl, h.Zone))
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key.Zone != null)
                .ThenBy(g => $"{g.Key.Lvl}|{g.Key.Zone}", StringComparer.Ordinal)
                .First();

            return new SheetTitle(best.Key.Lvl, best.Key.Zone, best.First().Raw);
        }
    }
}
