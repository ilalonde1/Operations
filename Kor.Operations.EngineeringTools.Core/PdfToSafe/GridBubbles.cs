#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// The grid bubbles on a sheet — a labelled circle with a grid axis through it — and the axes
    /// they name. A grid line is the line through a bubble; a circled column mark has no axis.
    /// </summary>
    /// <remarks>
    /// Measured on the five KOR jobs' schedule pages, 2026-09-08: on 31130 p11 and 31138 p9 every
    /// labelled circle has an axis through it (19 of 19, 15 of 15); on 31202 p17, which circles its
    /// column marks as well, 17 of 115 do and the other 98 are the marks. So the axis is what tells
    /// a grid bubble from a mark, by text alone they are the same numeral.
    ///
    /// A circle read with no curve segmentation is the four Bézier end points of its arcs, a
    /// diamond whose corners are equidistant from its centre; with segmentation it is a polygon
    /// whose vertices are. Either way: a closed path whose points all sit one radius from their
    /// centroid, holding exactly one short text token.
    ///
    /// WHAT THIS DOES NOT COVER: a grid drawn without bubbles (its lines then read as they did),
    /// and a bubble whose label is set outside the circle.
    /// </remarks>
    public static class GridBubbles
    {
        /// <param name="RuleX">The x of the longest vertical rule through the bubble — the axis's own position, which is not the circle's centre.</param>
        /// <param name="RuleY">The y of the longest horizontal rule through the bubble.</param>
        public readonly record struct Bubble(double Cx, double Cy, double Radius, string Label, bool OnVerticalAxis, bool OnHorizontalAxis,
            double? RuleX = null, double? RuleY = null)
        {
            public bool IsGridBubble => OnVerticalAxis || OnHorizontalAxis;
            public bool Contains(double x, double y) => (x - Cx) * (x - Cx) + (y - Cy) * (y - Cy) <= Radius * Radius;
        }

        /// <summary>
        /// A named grid axis, in points: the bubble's label and the rule through it. A vertical axis
        /// runs in y at x = <see cref="At"/> (ETABS DIR "X"); a horizontal one runs in x at y = At.
        /// <see cref="Bubbles"/> is how many bubbles named it — two when both ends carry one — and
        /// <see cref="LabelsDisagree"/> says the ends carried different labels, joined in the name by "|".
        /// </summary>
        public sealed record Axis(string Name, bool Vertical, double At, int Bubbles, bool LabelsDisagree);

        public sealed record Grid(IReadOnlyList<Bubble> Bubbles, IReadOnlyList<double> VerticalAxesX, IReadOnlyList<double> HorizontalAxesY,
            IReadOnlyList<Axis> Axes);

        /// <summary>A point is on the circle when its distance from the centroid is within this share of the radius.</summary>
        public const double RoundnessTolerance = 0.12;

        /// <summary>Bubbles are small: between these radii, in points.</summary>
        public const double MinRadiusPts = 4.0, MaxRadiusPts = 40.0;

        /// <summary>A rule this close to a bubble's centre runs through it.</summary>
        public const double AxisTolerancePts = 1.5;

        /// <summary>The rules through a bubble must cover at least this share of the page's shorter side to be a grid axis.</summary>
        public const double AxisMinShare = 0.25;

        public static Grid On(VectorPageReader.PageContent page)
        {
            ArgumentNullException.ThrowIfNull(page);
            var rules = ScheduleTableBorder.RulesOn(page);
            double reach = AxisMinShare * Math.Min(page.WidthPts, page.HeightPts);

            var bubbles = new List<Bubble>();
            foreach (var path in page.Paths)
            {
                if (path.IsAnnotation || !path.IsClosed || path.Points.Count < 4) continue;
                double w = path.Width, h = path.Height;
                if (w < 2 * MinRadiusPts || w > 2 * MaxRadiusPts) continue;
                if (Math.Abs(w - h) > 0.15 * Math.Max(w, h)) continue;

                double cx = (path.MinX + path.MaxX) / 2, cy = (path.MinY + path.MaxY) / 2, r = (w + h) / 4;
                if (path.Points.Any(p => Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - r) > RoundnessTolerance * r))
                    continue;

                var inside = page.Words.Where(t => (t.Cx - cx) * (t.Cx - cx) + (t.Cy - cy) * (t.Cy - cy) <= r * r).ToList();
                if (inside.Count != 1 || inside[0].Text.Trim().Length > 4) continue;

                var throughV = rules.Vertical.Where(v => Math.Abs(v.At - cx) <= AxisTolerancePts).ToList();
                var throughH = rules.Horizontal.Where(hr => Math.Abs(hr.At - cy) <= AxisTolerancePts).ToList();
                double vertical = throughV.Sum(v => v.Length);
                double horizontal = throughH.Sum(hr => hr.Length);
                // the axis is the rule, not the circle: its position is the rule NEAREST the centre
                // (audit F4). Nearest, not longest — a neighbouring grid line 1.5 pt away can be the
                // longer one, and taking it merged two grids into one axis.
                double? ruleX = vertical >= reach ? throughV.OrderBy(v => Math.Abs(v.At - cx)).ThenByDescending(v => v.Length).First().At : null;
                double? ruleY = horizontal >= reach ? throughH.OrderBy(hr => Math.Abs(hr.At - cy)).ThenByDescending(hr => hr.Length).First().At : null;
                bubbles.Add(new Bubble(cx, cy, r, inside[0].Text.Trim(), vertical >= reach, horizontal >= reach, ruleX, ruleY));
            }

            var vAxes = Axes(bubbles, vertical: true);
            var hAxes = Axes(bubbles, vertical: false);
            return new Grid(
                bubbles,
                vAxes.Select(a => a.At).ToList(),
                hAxes.Select(a => a.At).ToList(),
                vAxes.Concat(hAxes).ToList());
        }

        /// <summary>
        /// Two bubbles name one axis when their rules are the same rule: within this of each other, a
        /// drafter's jitter, not a second grid line. The floor under it is the rule reader's own:
        /// <see cref="ScheduleTableBorder.StraightPts"/> (0.6 pt, 20 mm at 1:96) merges pieces into
        /// one rule, so two grid lines closer than that are one axis whose ends disagree — and no
        /// building draws two grid lines 20 mm apart.
        /// </summary>
        public const double SameRulePts = 0.5;

        /// <summary>
        /// The named axes in one direction: bubbles on an axis, sorted by their RULE's coordinate,
        /// one axis per run of bubbles on the same rule (within <see cref="SameRulePts"/>) — the two
        /// ends of one grid line are one axis, named once; two rules 1.5 pt apart are two axes
        /// (audit F4: clustering the circles' centres merged them, and placed the axis at the circle).
        /// </summary>
        private static List<Axis> Axes(List<Bubble> bubbles, bool vertical)
        {
            var ends = bubbles.Where(b => vertical ? b.OnVerticalAxis : b.OnHorizontalAxis)
                .Select(b => (At: vertical ? (b.RuleX ?? b.Cx) : (b.RuleY ?? b.Cy), b.Label)).OrderBy(e => e.At).ToList();
            var axes = new List<Axis>();
            int start = 0;
            for (int i = 1; i <= ends.Count; i++)
            {
                if (i < ends.Count && ends[i].At - ends[i - 1].At <= SameRulePts) continue;
                var run = ends.GetRange(start, i - start);
                var labels = run.Select(e => e.Label).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
                axes.Add(new Axis(string.Join("|", labels), vertical, run.Average(e => e.At), run.Count, labels.Count > 1));
                start = i;
            }
            return axes;
        }
    }
}
