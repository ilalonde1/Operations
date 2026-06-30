#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Turns the vision schedule-reader JSON (column / wall schedule sheets) into the bands
    /// <see cref="ScheduleTakeoff.ComputeColumn"/> / <see cref="ScheduleTakeoff.ComputeWall"/> price.
    ///
    /// The column schedule states a size only at the levels where it CHANGES; the size holds for the floors
    /// in between. So a raw entry list has gaps. This reader fills each mark's size DOWN the level ladder
    /// from the topmost stated level to the bottom, emitting one band per level — deterministic, no guessing
    /// beyond the standard "a stated size holds until the next stated size below it" the schedule itself means.
    /// </summary>
    public static class ScheduleConcreteReader
    {
        private sealed class ColEntry
        {
            public string mark { get; set; } = "";
            public string levelTop { get; set; } = "";
            public string levelBottom { get; set; } = "";
            public double widthIn { get; set; }
            public double depthIn { get; set; }
        }

        private sealed class ColDoc { public List<ColEntry> entries { get; set; } = new(); }

        /// <summary>Order level labels top-to-bottom: LEVEL 19 … LEVEL 1, then P1, P2 … (parkade below grade).</summary>
        public static List<string> OrderLevels(IEnumerable<string> levels) =>
            levels.Where(l => !string.IsNullOrWhiteSpace(l))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(LevelKey).ThenBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();

        // Smaller key = higher up. Above-grade level N -> -N; parkade Pn -> +n (below all above-grade).
        private static double LevelKey(string lvl)
        {
            string u = (lvl ?? "").ToUpperInvariant();
            var pk = System.Text.RegularExpressions.Regex.Match(u, @"P\s*0*(\d+)");
            if (pk.Success) return 1000 + int.Parse(pk.Groups[1].Value);     // parkade, below grade
            var n = System.Text.RegularExpressions.Regex.Match(u, @"0*(\d+)");
            if (n.Success) return -int.Parse(n.Groups[1].Value);             // above grade, higher = first
            if (u.Contains("ROOF")) return -10000;                            // roof above everything
            return 0;
        }

        /// <summary>
        /// Column bands from the column-schedule JSON, filled down the FULL level ladder so every floor a
        /// column runs through is priced (one band per level per mark). The schedule states a size only where
        /// it changes, and never lists the floors in between — so the complete floor list must be supplied
        /// (from the schedule's own level column or the slab takeoff), not inferred from the change-points.
        /// Sizes are taken as read (inches); a mark with no positive size is skipped.
        /// </summary>
        public static List<ScheduleTakeoff.ColumnBand> ColumnBands(string json, IReadOnlyList<string> ladderTopToBottom)
        {
            ArgumentNullException.ThrowIfNull(ladderTopToBottom);
            var doc = JsonSerializer.Deserialize<ColDoc>(json) ?? new ColDoc();
            var entries = doc.entries.Where(e => !string.IsNullOrWhiteSpace(e.mark) && e.widthIn > 0).ToList();
            if (entries.Count == 0) return new();

            var ladder = ladderTopToBottom;
            // Normalize so "LEVEL 19" (vision JSON) and "L19" (deterministic ladder reader) match — the same
            // canonicalization ComputeColumn applies, so the bands we emit resolve consistently downstream.
            var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ladder.Count; i++) idx[ScheduleTakeoff.NormalizeLevel(ladder[i])] = i;

            var bands = new List<ScheduleTakeoff.ColumnBand>();
            foreach (var byMark in entries.GroupBy(e => e.mark, StringComparer.OrdinalIgnoreCase))
            {
                // Stated size at each ladder index this mark names (expanding any top..bottom range).
                var stated = new (double w, double d)?[ladder.Count];
                foreach (var e in byMark)
                {
                    if (!idx.TryGetValue(ScheduleTakeoff.NormalizeLevel(e.levelTop), out int a)
                        || !idx.TryGetValue(ScheduleTakeoff.NormalizeLevel(e.levelBottom), out int b)) continue;
                    if (a > b) (a, b) = (b, a);
                    double d = e.depthIn > 0 ? e.depthIn : e.widthIn;
                    for (int i = a; i <= b; i++) stated[i] = (e.widthIn, d);
                }

                // Fill down: from the topmost stated level, carry the last size to every floor below.
                int first = Array.FindIndex(stated, s => s.HasValue);
                if (first < 0) continue;
                (double w, double d)? carry = null;
                for (int i = first; i < ladder.Count; i++)
                {
                    if (stated[i].HasValue) carry = stated[i];
                    if (carry is { } c)
                        bands.Add(new ScheduleTakeoff.ColumnBand(byMark.Key, ladder[i], ladder[i], c.w, c.d));
                }
            }
            return bands;
        }

        /// <summary>The distinct level labels a column-schedule JSON names (its change-points) — NOT the full
        /// floor list. Use to widen a known ladder, or to sanity-check which floors the schedule touched.</summary>
        public static List<string> NamedLevels(string json)
        {
            var doc = JsonSerializer.Deserialize<ColDoc>(json) ?? new ColDoc();
            return OrderLevels(doc.entries.SelectMany(e => new[] { e.levelTop, e.levelBottom }));
        }
    }
}
