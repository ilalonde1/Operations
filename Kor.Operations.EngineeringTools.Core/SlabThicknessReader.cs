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
        private const int RecoverMaxMm = 1200; // recovery (mats only) admits a deep mat foundation (~48")
        private const double MmPerInch = 25.4;

        // RECOVERY anchors — a STRUCTURAL base-slab depth the field reader skips only because it isn't stated
        // in the bare «N" SLAB» shape: a transfer MAT or a RAFT. Deliberately NOT slab-on-grade / SOG / topping:
        // the field reader excludes those for a STRUCTURAL reason (a 4" unreinforced topping is not a suspended
        // slab), and re-admitting them here would let a thin SOG masquerade as a parkade slab's depth and
        // pre-empt the (thicker, correct) same-class peer estimate. Bare «N" SLAB» is also excluded — that is
        // the field reader's job, and excluding it keeps note-numbering ("5. SLABS") out of the recovery pool.
        private static readonly Regex RecoverImperialRx = new(
            @"(\d{1,2})\s*[^\w\s]{1,2}(?:\s+\w+){0,2}?\s+(?:MATS?|RAFT)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RecoverMetricRx = new(
            @"(\d{2,4})\s*(?:MM)?(?:\s+\w+){0,2}?\s+(?:MATS?|RAFT)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        /// <summary>
        /// The WIDER recovery read for a plate the field reader above came up empty on: a STRUCTURAL base-slab
        /// depth stated as a transfer MAT or a RAFT — the forms <see cref="DominantThicknessIn"/> skips only
        /// because they aren't the bare «N&quot; SLAB» shape. Returns the modal recovered depth (inches), or null
        /// when no such callout is on the plate (the caller then estimates from a same-class peer or leaves it an
        /// honest residual). DELIBERATELY does NOT read slab-on-grade / SOG / topping (a non-structural element
        /// the field reader excludes on purpose) nor bare «N&quot; SLAB» (the field reader's domain), so it only
        /// ADDS real structural depths the field read missed, and is fired only AFTER the field read fails.
        /// </summary>
        public static int? RecoverStructuralDepthIn(IEnumerable<string>? lines)
        {
            if (lines is null) return null;
            var imperial = new List<int>();
            var metricMm = new List<int>();
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                foreach (Match m in RecoverImperialRx.Matches(line))
                {
                    int n = int.Parse(m.Groups[1].Value);
                    if (n >= MinIn && n <= MaxIn) imperial.Add(n);
                }
                foreach (Match m in RecoverMetricRx.Matches(line))
                {
                    int n = int.Parse(m.Groups[1].Value);
                    if (n >= MinMm && n <= RecoverMaxMm) metricMm.Add(n);
                }
            }
            // Same metric-vs-imperial decision as the field reader: a metric sheet's matches win once they
            // at least tie the imperial count (the imperial side is then inch-mark noise).
            if (metricMm.Count >= 1 && metricMm.Count >= imperial.Count)
            {
                int modalMm = metricMm.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                return (int)Math.Round(modalMm / MmPerInch, MidpointRounding.AwayFromZero);
            }
            if (imperial.Count == 0) return null;
            return imperial.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        }
    }
}
