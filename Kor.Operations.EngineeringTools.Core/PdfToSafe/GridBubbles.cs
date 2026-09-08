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
        public readonly record struct Bubble(double Cx, double Cy, double Radius, string Label, bool OnVerticalAxis, bool OnHorizontalAxis)
        {
            public bool IsGridBubble => OnVerticalAxis || OnHorizontalAxis;
            public bool Contains(double x, double y) => (x - Cx) * (x - Cx) + (y - Cy) * (y - Cy) <= Radius * Radius;
        }

        public sealed record Grid(IReadOnlyList<Bubble> Bubbles, IReadOnlyList<double> VerticalAxesX, IReadOnlyList<double> HorizontalAxesY);

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

                double vertical = rules.Vertical.Where(v => Math.Abs(v.At - cx) <= AxisTolerancePts).Sum(v => v.Length);
                double horizontal = rules.Horizontal.Where(hr => Math.Abs(hr.At - cy) <= AxisTolerancePts).Sum(hr => hr.Length);
                bubbles.Add(new Bubble(cx, cy, r, inside[0].Text.Trim(), vertical >= reach, horizontal >= reach));
            }

            return new Grid(
                bubbles,
                bubbles.Where(b => b.OnVerticalAxis).Select(b => b.Cx).Distinct().OrderBy(x => x).ToList(),
                bubbles.Where(b => b.OnHorizontalAxis).Select(b => b.Cy).Distinct().OrderBy(y => y).ToList());
        }
    }
}
