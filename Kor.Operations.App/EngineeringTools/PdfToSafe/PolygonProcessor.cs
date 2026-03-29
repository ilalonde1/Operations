using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal static class PolygonProcessor
    {
        /// <summary>Euclidean distance between two mm-coordinate points.</summary>
        public static double Distance((double X, double Y) a, (double X, double Y) b)
            => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

        /// <summary>Total perimeter length of a polyline (mm).</summary>
        public static double PathLength(List<(double X, double Y)> pts)
        {
            double len = 0;
            for (int i = 1; i < pts.Count; i++) len += Distance(pts[i - 1], pts[i]);
            return len;
        }

        /// <summary>Total length of an open polyline in mm (alias of PathLength for clarity).</summary>
        public static double PolylineLengthMm(List<(double X, double Y)> pts)
            => PathLength(pts);

        /// <summary>Centroid of a polyline (weighted by segment length).</summary>
        public static (double X, double Y) Centroid(List<(double X, double Y)> pts)
        {
            if (pts.Count == 0) return (0, 0);
            double sumX = 0, sumY = 0, total = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                double w = Distance(pts[i - 1], pts[i]);
                sumX += (pts[i - 1].X + pts[i].X) * 0.5 * w;
                sumY += (pts[i - 1].Y + pts[i].Y) * 0.5 * w;
                total += w;
            }
            return total > 0 ? (sumX / total, sumY / total) : (pts[0].X, pts[0].Y);
        }

        /// <summary>Signed area of a closed polygon via shoelace formula (mm). Always returns positive.</summary>
        public static double PolygonAreaMm2(List<(double X, double Y)> pts)
        {
            if (pts.Count < 3) return 0;
            double area = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += pts[i].X * pts[j].Y;
                area -= pts[j].X * pts[i].Y;
            }
            return Math.Abs(area) / 2.0;
        }

        /// <summary>Area of a triangle (mm).</summary>
        public static double TriangleArea(
            (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
            => 0.5 * Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y));

        /// <summary>
        /// Douglas-Peucker polyline simplification.
        /// Removes points where perpendicular deviation is less than epsilonMm.
        /// </summary>
        public static List<(double X, double Y)> DouglasPeucker(
            List<(double X, double Y)> pts, double epsilonMm)
        {
            if (pts.Count <= 2) return new List<(double, double)>(pts);
            double maxDist = 0;
            int maxIdx = 0;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                double d = PerpendicularDistance(pts[i], pts[0], pts[^1]);
                if (d > maxDist) { maxDist = d; maxIdx = i; }
            }
            if (maxDist <= epsilonMm)
                return new List<(double, double)> { pts[0], pts[^1] };
            var left = DouglasPeucker(pts[..maxIdx], epsilonMm);
            var right = DouglasPeucker(pts[maxIdx..], epsilonMm);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        /// <summary>
        /// Decomposes an N-gon into triangles using fan triangulation from vertex 0.
        /// Only triangles with area > minAreaMm2 are kept.
        /// </summary>
        public static List<List<(double X, double Y)>> FanTriangulate(
            List<(double X, double Y)> pts, double minAreaMm2 = PdfToSafeConstants.MinTriangleAreaMm2)
        {
            var result = new List<List<(double X, double Y)>>();
            for (int i = 1; i < pts.Count - 1; i++)
            {
                if (TriangleArea(pts[0], pts[i], pts[i + 1]) > minAreaMm2)
                    result.Add(new List<(double X, double Y)> { pts[0], pts[i], pts[i + 1] });
            }
            return result;
        }

        /// <summary>
        /// Deduplicates near-identical consecutive points and removes NaN/Infinity coordinates.
        /// </summary>
        public static List<(double X, double Y)> FilterPoints(
            List<(double X, double Y)> raw,
            double minDistanceMm = PdfToSafeConstants.MinVertexDistanceMm)
        {
            var result = new List<(double X, double Y)>();
            foreach (var p in raw)
            {
                if (double.IsNaN(p.X) || double.IsInfinity(p.X) ||
                    double.IsNaN(p.Y) || double.IsInfinity(p.Y)) continue;
                if (result.Count == 0 || Distance(result[^1], p) >= minDistanceMm)
                    result.Add(p);
            }
            return result;
        }

        /// <summary>Returns true if pt is inside the polygon (ray-casting).</summary>
        public static bool PointInPolygon((double X, double Y) pt, List<(double X, double Y)> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if ((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y) &&
                    pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) /
                           (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Processes a collection of raw slab polygons: filters degenerate points,
        /// simplifies with Douglas-Peucker, and fan-triangulates quads+ into triangles.
        /// Returns parallel lists of processed polygons and their colors.
        /// </summary>
        public static (List<List<(double X, double Y)>> Slabs,
                      List<(byte R, byte G, byte B)> Colors)
            ProcessSlabs(
                List<List<(double X, double Y)>> rawSlabs,
                List<(byte R, byte G, byte B)> rawColors,
                double epsilonMm = PdfToSafeConstants.DouglasPeuckerEpsilonMm,
                double minTriAreaMm2 = PdfToSafeConstants.MinTriangleAreaMm2)
        {
            var slabs = new List<List<(double X, double Y)>>(rawSlabs);
            var colors = new List<(byte R, byte G, byte B)>(rawColors);

            for (int i = slabs.Count - 1; i >= 0; i--)
            {
                var s = DouglasPeucker(slabs[i], epsilonMm);
                if (s.Count >= 3) slabs[i] = s;
                else { slabs.RemoveAt(i); colors.RemoveAt(i); }
            }

            var decomposed = new List<List<(double X, double Y)>>();
            var decomposedColors = new List<(byte R, byte G, byte B)>();
            for (int i = 0; i < slabs.Count; i++)
            {
                if (slabs[i].Count <= 4)
                {
                    decomposed.Add(slabs[i]);
                    decomposedColors.Add(colors[i]);
                }
                else
                {
                    foreach (var tri in FanTriangulate(slabs[i], minTriAreaMm2))
                    {
                        decomposed.Add(tri);
                        decomposedColors.Add(colors[i]);
                    }
                }
            }
            return (decomposed, decomposedColors);
        }

        private static double PerpendicularDistance(
            (double X, double Y) pt,
            (double X, double Y) lineStart,
            (double X, double Y) lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-10) return Distance(pt, lineStart);
            return Math.Abs(dy * pt.X - dx * pt.Y + lineEnd.X * lineStart.Y - lineEnd.Y * lineStart.X) / len;
        }
    }
}
