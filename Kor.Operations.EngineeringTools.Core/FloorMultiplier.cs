#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Layer 1 (exact, NO AI): how many physical floors one "typical" plan represents. A residential
    /// tower draws one plan for a band of identical floors and states the band as exact text on the
    /// sheet (or its reinforcing sibling) — e.g. "LEVEL 4 - 13 SLABS", "STUD RAIL SCHEDULE - LEVEL 17 -
    /// 28 SLAB REINFORCING". The estimator multiplies that plan's concrete by the floor count. This reads
    /// the count off the drawing's own text; it never guesses a number. Parkade/roof levels (1:1) and any
    /// level with no stated band return 1.
    /// </summary>
    public static class FloorMultiplier
    {
        // "LEVEL 4 - 13", "LEVELS 17 – 28" — a level band: two storey numbers joined by a dash. Hyphen,
        // en-dash and em-dash all occur in CAD title text.
        private static readonly Regex BandRx = new(
            @"LEVELS?\s+(\d{1,2})\s*[-–—]\s*(\d{1,2})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Sanity bounds: a real residential level band sits within these; anything wider is noise
        // (a coincidental dash between unrelated numbers, a whole-building reference, etc.).
        private const int MaxLevel = 70;
        private const int MaxBandSpan = 45;

        /// <summary>All valid (low, high) level bands stated anywhere in <paramref name="lines"/>.</summary>
        public static IReadOnlyList<(int Low, int High)> Bands(IEnumerable<string>? lines)
        {
            var result = new List<(int, int)>();
            if (lines is null) return result;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                foreach (Match m in BandRx.Matches(line))
                {
                    int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
                    if (a > b) (a, b) = (b, a);
                    if (a < 1 || b > MaxLevel || (b - a + 1) > MaxBandSpan) continue;
                    result.Add((a, b));
                }
            }
            return result;
        }

        /// <summary>
        /// Physical floor count for the typical plan that represents <paramref name="repLevel"/>: the
        /// widest stated band that CONTAINS that level (covers both the "named after the top floor"
        /// convention, e.g. plan "13" for band 4–13, and "named after the bottom", e.g. plan "44" for
        /// band 44–45). Returns 1 when no band contains it — a conservative, never-inflating default.
        /// </summary>
        public static int CountForLevel(IEnumerable<string>? lines, int repLevel)
        {
            var containing = Bands(lines).Where(b => b.Low <= repLevel && repLevel <= b.High).ToList();
            return containing.Count == 0 ? 1 : containing.Max(b => b.High - b.Low + 1);
        }
    }
}
