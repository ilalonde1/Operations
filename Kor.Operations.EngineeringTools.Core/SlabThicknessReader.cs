#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Layer 1 (exact, NO AI): the slab's field thickness, read off the drawing's own callouts ("10&quot;
    /// SLAB", "12&quot; SLABS") instead of from a synthesised image read — so it is STABLE run-to-run, not
    /// the ±noise an image read gives. The callout is a storey number immediately followed by an inch
    /// mark and the word SLAB; that tight shape naturally skips the distractors an estimator also skips:
    /// "4&quot; UNREINFORCED SLAB ON GRADE" (a word intervenes) and "12&quot; PC3 … SLAB" (a column). Where a
    /// sheet states more than one (the field slab plus thicker slab-bands/drops), the MOST COMMON wins,
    /// and ties go to the thinner value — the field slab is called out far more than its thickenings.
    /// </summary>
    public static class SlabThicknessReader
    {
        // IMPERIAL: "<n>" SLAB" — a storey number, a 1–2 char inch mark (the glyph varies on CAD sheets),
        // then SLAB. The inch mark is REQUIRED, which is what keeps a metric "200 SLAB" out of this pool.
        private static readonly Regex ImperialRx = new(@"(\d{1,2})\s*[^\w\s]{1,2}\s*SLAB",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // METRIC: "<mm> SLAB" — a 2–3 digit millimetre depth directly before SLAB (no inch mark). The
        // required adjacency keeps an imperial «10" SLAB» (inch mark intervenes) and note-numbering
        // («5. SLABS») out of this pool. Metric drawings (e.g. 5380 Heather) call out "200 SLAB".
        private static readonly Regex MetricRx = new(@"(\d{2,3})\s*SLAB",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const int MinIn = 4;     // thinner than this is a topping/SOG, not a structural slab
        private const int MaxIn = 48;    // thicker is a transfer mat, not a field slab
        private const int MinMm = 100;   // ~4"  — plausible structural slab in millimetres
        private const int MaxMm = 600;   // ~24" — above this is a transfer/mat, not a field slab
        private const double MmPerInch = 25.4;

        /// <summary>
        /// The dominant field-slab thickness (inches) called out across the lines, or null. Reads BOTH
        /// imperial («10&quot; SLAB») and metric («200 SLAB») callouts. A drawing is one or the other: when
        /// metric callouts have at least as much support, the sheet is metric and the imperial matches are
        /// note-numbering noise («5. SLABS»), so the metric modal wins (converted to inches).
        /// </summary>
        public static int? DominantThicknessIn(IEnumerable<string>? lines)
        {
            if (lines is null) return null;
            var imperial = new List<int>();
            var metricMm = new List<int>();
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                foreach (Match m in ImperialRx.Matches(line))
                {
                    int n = int.Parse(m.Groups[1].Value);
                    if (n >= MinIn && n <= MaxIn) imperial.Add(n);
                }
                foreach (Match m in MetricRx.Matches(line))
                {
                    int n = int.Parse(m.Groups[1].Value);
                    if (n >= MinMm && n <= MaxMm) metricMm.Add(n);
                }
            }

            // Metric drawing: ≥2 metric callouts that at least match the imperial count (imperial here is
            // note-numbering noise). Convert the modal mm to inches.
            if (metricMm.Count >= 2 && metricMm.Count >= imperial.Count)
            {
                int modalMm = metricMm.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                return (int)Math.Round(modalMm / MmPerInch, MidpointRounding.AwayFromZero);
            }
            if (imperial.Count == 0) return null;
            return imperial.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        }
    }
}
