#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// High-level rebar weight: density (kg/m³) x concrete volume (m³) per element.
    /// Densities are defensible standard reinforcing ratios, calibratable, and corroborated
    /// against the reinforcing actually extracted from the drawings (see Corroborate).
    /// This is the "rough / high-level takeoff" the client asked for - NOT a bar-bending schedule.
    /// </summary>
    public static class RebarWeightEstimator
    {
        public static readonly IReadOnlyList<string> Elements =
            new[] { "Slab", "Wall", "Column", "Foundation" };

        public static IReadOnlyDictionary<string, double> DefaultDensities =>
            new Dictionary<string, double>
            {
                ["Slab"] = 120,
                ["Wall"] = 130,
                ["Column"] = 290,
                ["Foundation"] = 90,
            };

        // Keep a per-issue density within this band of the standard ratio. A rough takeoff
        // should not swing wildly off one noisy extraction.
        private const double RatioFloor = 0.70;
        private const double RatioCeil = 1.45;

        public static RebarWeightResult Estimate(
            IReadOnlyDictionary<string, double> volBefore,
            IReadOnlyDictionary<string, double> volAfter,
            IReadOnlyDictionary<string, double>? densities = null,
            IReadOnlyDictionary<string, string>? corroboration = null,
            string beforeLabel = "Before",
            string afterLabel = "After",
            IReadOnlyDictionary<string, double>? intensityBefore = null,
            IReadOnlyDictionary<string, double>? intensityAfter = null)
        {
            densities ??= DefaultDensities;
            var lines = new List<RebarWeightLine>();
            foreach (var el in Elements)
            {
                double std = densities.GetValueOrDefault(el, 0);
                double vb = volBefore.GetValueOrDefault(el, 0);
                double va = volAfter.GetValueOrDefault(el, 0);

                // Scale the standard density per issue by that issue's reinforcing intensity,
                // anchored so the average of the two issues stays on the standard ratio.
                double ib = intensityBefore?.GetValueOrDefault(el, 0) ?? 0;
                double ia = intensityAfter?.GetValueOrDefault(el, 0) ?? 0;
                double db = std, da = std;
                string note = "intensity n/a — held at standard ratio";
                if (ib > 0 && ia > 0)
                {
                    double mean = (ib + ia) / 2.0;
                    double rb = Math.Clamp(ib / mean, RatioFloor, RatioCeil);
                    double ra = Math.Clamp(ia / mean, RatioFloor, RatioCeil);
                    db = std * rb;
                    da = std * ra;
                    note = $"call-out intensity {ib:0.0}→{ia:0.0} kg/m² ⇒ density {db:0}→{da:0} kg/m³";
                }

                double tb = db * vb / 1000.0;
                double ta = da * va / 1000.0;
                lines.Add(new RebarWeightLine(
                    el, std, db, vb, tb, da, va, ta, ta - tb,
                    note, corroboration?.GetValueOrDefault(el, "") ?? ""));
            }
            return new RebarWeightResult(
                lines,
                lines.Sum(l => l.TonnesBefore),
                lines.Sum(l => l.TonnesAfter),
                lines.Sum(l => l.DeltaTonnes),
                beforeLabel, afterLabel);
        }

        /// <summary>
        /// Reinforcing intensity index per element (kg/m² of steel, roughly) read from the call-outs
        /// on one issue's pages. The ABSOLUTE value isn't a takeoff - it's a comparator: the ratio of
        /// before-index to after-index is what scales the rough density, so the weight reflects the
        /// detailing changes rather than concrete volume alone.
        /// </summary>
        public static IReadOnlyDictionary<string, double> CalloutIntensity(IReadOnlyList<string> pages)
        {
            string all = string.Join("\n", pages);

            // Frequency-weighted mean of barMass(size)/spacing(m) over a set of "size M @ spacing" matches.
            double MeanMassPerSpacing(Regex re, int sMin, int sMax, int szMin, int szMax)
            {
                double wsum = 0, nsum = 0;
                var groups = re.Matches(all)
                    .Select(m => (sz: int.Parse(m.Groups[1].Value), sp: int.Parse(m.Groups[2].Value)))
                    .Where(x => x.sp >= sMin && x.sp <= sMax && x.sz >= szMin && x.sz <= szMax)
                    .GroupBy(x => (x.sz, x.sp));
                foreach (var g in groups)
                {
                    if (!BarMassKgM.TryGetValue(g.Key.sz, out double mass)) continue;
                    int freq = g.Count();
                    wsum += freq * (mass / (g.Key.sp / 1000.0)); // kg/m per m width = kg/m²
                    nsum += freq;
                }
                return nsum > 0 ? wsum / nsum : 0;
            }

            // Slab: top+bottom, two ways -> ~2 mats. Wall: vert + horiz, each face -> ~2 layers.
            double slab = 2.0 * MeanMassPerSpacing(Mat, 100, 400, 10, 20);
            double wallV = MeanMassPerSpacing(WallVert, 75, 400, 10, 30);
            double wallH = MeanMassPerSpacing(WallHoriz, 75, 400, 10, 30);
            double wall = (wallV + wallH); // both faces folded into the two directions

            // Column: vertical bar steel per metre of column (n × barMass), freq-weighted.
            double colW = 0, colN = 0;
            foreach (var g in Vert.Matches(all)
                         .Select(m => (n: int.Parse(m.Groups[1].Value), sz: int.Parse(m.Groups[2].Value)))
                         .Where(v => v.n >= 4 && v.n <= 24 && v.sz >= 15 && v.sz <= 45)
                         .GroupBy(v => (v.n, v.sz)))
            {
                if (!BarMassKgM.TryGetValue(g.Key.sz, out double mass)) continue;
                colW += g.Count() * (g.Key.n * mass);
                colN += g.Count();
            }
            double col = colN > 0 ? colW / colN : 0;

            return new Dictionary<string, double>
            {
                ["Slab"] = slab,
                ["Wall"] = wall,
                ["Column"] = col,
                ["Foundation"] = 0, // not reliably extractable -> held at standard ratio
            };
        }

        // CSA bar mass (kg/m) - shown on the report for transparency.
        public static readonly IReadOnlyDictionary<int, double> BarMassKgM =
            new Dictionary<int, double>
            { [10] = 0.785, [15] = 1.570, [20] = 2.355, [25] = 3.925, [30] = 5.495, [35] = 7.850, [45] = 11.775, [55] = 19.625 };

        private static readonly Regex Vert = new(@"(\d{1,2})-(\d{2})M\s*VERT", RegexOptions.Compiled);
        private static readonly Regex Ties = new(@"(\d{2})M\s*@\s*(\d{2,3})\s*TIES", RegexOptions.Compiled);
        private static readonly Regex WallTh = new(@"(\d{3})\s*WALL", RegexOptions.Compiled);
        private static readonly Regex WallVert = new(@"(\d{2})M\s*@\s*(\d{2,3})\s*VERT", RegexOptions.Compiled);
        private static readonly Regex WallHoriz = new(@"(\d{2})M\s*@\s*(\d{2,3})\s*HORIZ", RegexOptions.Compiled);
        private static readonly Regex Mat = new(@"\b(\d{2})M\s*@\s*(\d{2,4})\b", RegexOptions.Compiled);

        /// <summary>
        /// Builds a short "consistent with the drawings" string per element from the reinforcing
        /// we can reliably extract - so the densities are visibly grounded, not pulled from air.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Corroborate(IReadOnlyList<string> pages)
        {
            string all = string.Join("\n", pages);

            var verts = Vert.Matches(all)
                .Select(m => (n: int.Parse(m.Groups[1].Value), s: int.Parse(m.Groups[2].Value)))
                .Where(v => v.n >= 4 && v.n <= 24 && v.s >= 15 && v.s <= 45) // plausible column verticals
                .Select(v => $"{v.n}-{v.s}M").ToList();
            var ties = Ties.Matches(all).Select(m => $"{m.Groups[1].Value}M@{m.Groups[2].Value}").Distinct().ToList();

            List<string> TopSpec(Regex re, int take) => re.Matches(all)
                .Select(m => $"{m.Groups[1].Value}M@{m.Groups[2].Value}")
                .GroupBy(x => x).OrderByDescending(g => g.Count()).Take(take).Select(g => g.Key).ToList();
            var wallTh = WallTh.Matches(all).Select(m => m.Groups[1].Value)
                .GroupBy(x => x).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key + "mm").ToList();
            var wallV = TopSpec(WallVert, 2);
            var wallH = TopSpec(WallHoriz, 2);

            var slabTh = SlabThicknessCallout.MatchMetricCorroborationTextMm(all).Select(mm => mm.ToString())
                .GroupBy(x => x).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key + "mm").ToList();
            var mats = Mat.Matches(all)
                .Where(m => { int s = int.Parse(m.Groups[2].Value); return s >= 100 && s <= 400; })
                .Select(m => $"{m.Groups[1].Value}M@{m.Groups[2].Value}")
                .GroupBy(x => x).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key).ToList();

            string VertRange()
            {
                if (verts.Count == 0) return "n/a";
                var sizes = verts.Select(v => int.Parse(v.Split('-')[1].TrimEnd('M'))).ToList();
                var counts = verts.Select(v => int.Parse(v.Split('-')[0])).ToList();
                return $"{counts.Min()}-{sizes.Min()}M to {counts.Max()}-{sizes.Max()}M verticals";
            }

            return new Dictionary<string, string>
            {
                ["Slab"] = slabTh.Count > 0
                    ? $"slabs {string.Join("/", slabTh)}; typical mats {string.Join(", ", mats)}"
                    : "typical two-way mat",
                ["Wall"] = (wallTh.Count > 0 || wallV.Count > 0)
                    ? $"walls {string.Join("/", wallTh)}; vert {string.Join(", ", wallV)} EF, horiz {string.Join(", ", wallH)} EF"
                    : "shear cores + perimeter walls",
                ["Column"] = verts.Count > 0
                    ? $"{VertRange()}; ties {string.Join(", ", ties.Take(3))}"
                    : "schedule columns",
                ["Foundation"] = "footings / mat (standard ratio)",
            };
        }
    }
}
