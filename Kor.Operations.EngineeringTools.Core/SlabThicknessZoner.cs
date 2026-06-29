#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// Layer 2 refinement (exact numbers, geometry for location only): a slab floor is rarely ONE
    /// thickness. A tower plate is large "8&quot; SLAB" wings plus "12&quot; SLAB" zones at the core/heavy
    /// bays; pricing the whole plate at a single modal thickness under-counts the thicker zones — a
    /// real chunk of the slab gap. This reads the «N&quot; SLAB» callouts WITH their positions (so a
    /// downstream Voronoi split can ask which thickness governs each pixel) and decides whether a plate
    /// is genuinely multi-zone or a single thickness with stray callouts.
    ///
    /// SAFETY: a floor only splits when a second value has REAL callout support (≥2 callouts and a fair
    /// share of the modal's count). A uniform parkade slab (one dominant "10&quot;" with a singleton stray)
    /// stays uniform — a lone callout must never carve a Voronoi cell it does not own. Thickenings/bands
    /// (>16&quot;, e.g. "24&quot; SLAB") are excluded from the field average; they are localized add-ons,
    /// handled/flagged separately, not part of the field slab thickness.
    /// </summary>
    public static class SlabThicknessZoner
    {
        /// <summary>One slab-thickness callout: its value (inches) and its anchor in PDF points (y-up).</summary>
        public readonly record struct Callout(double Cx, double Cy, int ValueIn);

        private const int MinIn = 4;        // thinner is a topping/SOG, not a structural slab
        private const int MaxIn = 48;       // capture up to a thickening/band so the decision can see it
        private const int FieldMaxIn = 16;  // a true field slab; above this is a localized thickening/mat

        // Words immediately left of SLAB on the same baseline, ending in «<n>" » — the same tight shape
        // SlabThicknessReader uses, so "UNREINFORCED SLAB" / "PC3 … SLAB" are skipped (a word intervenes).
        private static readonly Regex TailRx = new(@"(\d{1,2})\s*[^\w\s]{1,2}\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Metric tail: a 2–3 digit millimetre depth directly before SLAB (no inch mark) — "200 SLAB",
        // "900 SLAB". Mirrors SlabThicknessReader's metric handling so a metric set zones too.
        private static readonly Regex MetricTailRx = new(@"(\d{2,3})\s*$", RegexOptions.Compiled);
        private const int MinMm = 100, MaxMm = 1200;
        private const double MmPerInch = 25.4;

        private const double BaselineTolPt = 6.0;    // same-line tolerance (matches ReadTextLines)
        private const double LeftReachPt = 60.0;     // how far left of SLAB to look for its number

        /// <summary>
        /// Read every «N&quot; SLAB» callout on the page with its anchor position, by pairing each SLAB word
        /// with the inch-marked number immediately to its left on the same baseline.
        /// </summary>
        public static IReadOnlyList<Callout> ReadCallouts(VectorPageReader.PageContent? page)
        {
            if (page is null) return Array.Empty<Callout>();
            var words = page.Words;
            var callouts = new List<Callout>();
            foreach (var slab in words)
            {
                var t = slab.Text?.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                // SLAB / SLABS, allowing trailing punctuation ("SLAB," "SLAB.").
                if (!t.TrimEnd('.', ',', ';', ':').Equals("SLAB", StringComparison.OrdinalIgnoreCase) &&
                    !t.TrimEnd('.', ',', ';', ':').Equals("SLABS", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Join the words just to the left on this baseline, left→right, then test the tight tail.
                var left = words
                    .Where(w => Math.Abs(w.Cy - slab.Cy) <= BaselineTolPt
                                && w.MaxX <= slab.MinX + 1
                                && w.MaxX >= slab.MinX - LeftReachPt)
                    .OrderBy(w => w.Cx)
                    .ToList();
                if (left.Count == 0) continue;
                string ctx = string.Join(" ", left.Select(w => w.Text));

                // Imperial («10" SLAB») first; if there is no inch mark, try a metric mm tail («200 SLAB»).
                int v;
                var m = TailRx.Match(ctx);
                if (m.Success) v = int.Parse(m.Groups[1].Value);
                else
                {
                    var mm = MetricTailRx.Match(ctx);
                    if (!mm.Success) continue;
                    int mmVal = int.Parse(mm.Groups[1].Value);
                    if (mmVal < MinMm || mmVal > MaxMm) continue;
                    v = (int)Math.Round(mmVal / MmPerInch, MidpointRounding.AwayFromZero);
                }
                if (v < MinIn || v > MaxIn) continue;
                callouts.Add(new Callout(slab.Cx, slab.Cy, v));
            }
            return callouts;
        }

        /// <summary>
        /// The field-thickness values that genuinely zone this plate: the modal value, plus any other
        /// field value (≤16&quot;) with real callout support. Returns just the modal when the floor is
        /// effectively one thickness — the caller then prices it exactly as before (no-op, safe).
        /// </summary>
        public static IReadOnlyList<int> QualifyingValues(IReadOnlyList<Callout> callouts, int modalThicknessIn)
        {
            var qualifying = new List<int> { modalThicknessIn };
            if (callouts is null || callouts.Count == 0) return qualifying;

            var field = callouts
                .Where(c => c.ValueIn >= MinIn && c.ValueIn <= FieldMaxIn)
                .GroupBy(c => c.ValueIn)
                .ToDictionary(g => g.Key, g => g.Count());
            int modalCount = field.TryGetValue(modalThicknessIn, out var mc) ? mc : 0;

            foreach (var kv in field.OrderBy(k => k.Key))
            {
                if (kv.Key == modalThicknessIn) continue;
                // Real support: at least two callouts AND a fair share of the modal's count — not a stray.
                if (kv.Value >= 2 && kv.Value >= 0.3 * Math.Max(1, modalCount))
                    qualifying.Add(kv.Key);
            }
            return qualifying;
        }

        /// <summary>
        /// The area-weighted effective field thickness (inches) from the per-thickness pixel areas of a
        /// Voronoi split (<see cref="PlanGeometry.ThicknessZoneFractions"/>). Falls back to the modal
        /// value when there is no genuine split (one zone, or no pixels) — so a uniform slab is unchanged.
        /// </summary>
        public static double EffectiveThicknessIn(IReadOnlyDictionary<int, long>? zonePx, int modalThicknessIn)
        {
            if (zonePx is null || zonePx.Count == 0) return modalThicknessIn;
            long total = zonePx.Values.Sum();
            if (total <= 0) return modalThicknessIn;
            if (zonePx.Count == 1) return zonePx.Keys.First();   // single zone — exact, no averaging needed
            double weighted = zonePx.Sum(kv => (double)kv.Key * kv.Value) / total;
            return weighted;
        }
    }
}
