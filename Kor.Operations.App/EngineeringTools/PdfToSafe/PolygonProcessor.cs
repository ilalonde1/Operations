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

        /// <summary>
        /// Area centroid of a closed polygon via the shoelace formula.
        /// Falls back to <see cref="Centroid"/> for degenerate (zero-area) polygons.
        /// </summary>
        public static (double X, double Y) PolygonAreaCentroid(List<(double X, double Y)> pts)
        {
            if (pts.Count < 3) return Centroid(pts);
            double area2 = 0, cx = 0, cy = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double cross = pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
                area2 += cross;
                cx += (pts[i].X + pts[j].X) * cross;
                cy += (pts[i].Y + pts[j].Y) * cross;
            }
            if (Math.Abs(area2) < 1e-10) return Centroid(pts);
            return (cx / (3.0 * area2), cy / (3.0 * area2));
        }

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

        public static bool PointInPolygon(
            (double X, double Y) pt,
            List<(double X, double Y)> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i].X, yi = poly[i].Y;
                double xj = poly[j].X, yj = poly[j].Y;
                bool intersect = ((yi > pt.Y) != (yj > pt.Y)) &&
                    (pt.X < (xj - xi) * (pt.Y - yi) / (yj - yi) + xi);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        public static List<(int ParentIndex, int ChildIndex)> DetectOpenings(
            List<List<(double X, double Y)>> polygons)
        {
            var result = new List<(int, int)>();
            for (int child = 0; child < polygons.Count; child++)
            {
                var childPoly = polygons[child];
                if (childPoly.Count < 3) continue;
                double childArea = PolygonAreaMm2(childPoly);

                for (int parent = 0; parent < polygons.Count; parent++)
                {
                    if (parent == child) continue;
                    var parentPoly = polygons[parent];
                    if (PolygonAreaMm2(parentPoly) <= childArea) continue;

                    bool allInside = true;
                    foreach (var pt in childPoly)
                    {
                        if (!PointInPolygon(pt, parentPoly)) { allInside = false; break; }
                    }
                    if (allInside)
                    {
                        result.Add((parent, child));
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Processes a collection of raw slab polygons: filters degenerate points
        /// and simplifies with Douglas-Peucker.
        /// Returns parallel lists of processed polygons and their colors.
        /// </summary>
        public static (List<List<(double X, double Y)>> Slabs,
                      List<(byte R, byte G, byte B)> Colors)
            ProcessSlabs(
                List<List<(double X, double Y)>> rawSlabs,
                List<(byte R, byte G, byte B)> rawColors,
                double epsilonMm = PdfToSafeConstants.DouglasPeuckerEpsilonMm)
        {
            var slabs = new List<List<(double X, double Y)>>(rawSlabs);
            var colors = new List<(byte R, byte G, byte B)>(rawColors);

            for (int i = slabs.Count - 1; i >= 0; i--)
            {
                var s = DouglasPeucker(slabs[i], epsilonMm);
                if (s.Count >= 3) slabs[i] = s;
                else { slabs.RemoveAt(i); colors.RemoveAt(i); }
            }

            return (slabs, colors);
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
