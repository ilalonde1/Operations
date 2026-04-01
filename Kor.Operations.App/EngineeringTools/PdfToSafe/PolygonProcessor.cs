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
            var left = DouglasPeucker(pts[..(maxIdx + 1)], epsilonMm);
            var right = DouglasPeucker(pts[maxIdx..], epsilonMm);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        /// <summary>
        /// Returns a CCW-oriented copy of the polygon (positive signed area in Y-up space).
        /// If the polygon is already CCW the same list is returned unchanged.
        /// </summary>
        public static List<(double X, double Y)> EnsureCCW(List<(double X, double Y)> pts)
        {
            if (pts.Count < 3) return pts;
            double area2 = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area2 += pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
            }
            if (area2 >= 0) return pts;
            var rev = new List<(double X, double Y)>(pts);
            rev.Reverse();
            return rev;
        }

        /// <summary>
        /// Douglas-Peucker simplification for a closed polygon ring.
        /// Evaluates all edges including the closing segment pts[^1]pts[0].
        /// </summary>
        public static List<(double X, double Y)> DouglasPeuckerClosed(
            List<(double X, double Y)> pts, double epsilonMm)
        {
            if (pts.Count <= 3) return new List<(double, double)>(pts);
            // Temporarily close the ring by appending the first point, run open D-P, then remove it
            var closed = new List<(double X, double Y)>(pts) { pts[0] };
            var simplified = DouglasPeucker(closed, epsilonMm);
            // Remove the appended duplicate closing point
            if (simplified.Count > 0 &&
                Math.Abs(simplified[^1].X - simplified[0].X) < 1e-9 &&
                Math.Abs(simplified[^1].Y - simplified[0].Y) < 1e-9)
                simplified.RemoveAt(simplified.Count - 1);
            return simplified;
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
            int wn = 0;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double yi = poly[i].Y, yj = poly[j].Y;
                if (yj <= pt.Y)
                {
                    if (yi > pt.Y)
                        if (IsLeft(poly[j], poly[i], pt) > 0) wn++;
                }
                else
                {
                    if (yi <= pt.Y)
                        if (IsLeft(poly[j], poly[i], pt) < 0) wn--;
                }
            }
            return wn != 0;
        }

        /// <summary>
        /// Returns positive if pt is left of the directed line p0p1,
        /// zero if on the line, negative if right.
        /// </summary>
        private static double IsLeft(
            (double X, double Y) p0,
            (double X, double Y) p1,
            (double X, double Y) pt)
            => (p1.X - p0.X) * (pt.Y - p0.Y) - (pt.X - p0.X) * (p1.Y - p0.Y);

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

                    var (ccx, ccy) = PolygonAreaCentroid(childPoly);
                    if (PointInPolygon((ccx, ccy), parentPoly))
                    {
                        result.Add((parent, child));
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Detects drop panels: closed polygons whose bounding-box diagonal is between
        /// <paramref name="minDiagMm"/> and <paramref name="maxDiagMm"/>, whose centroid
        /// lies within <paramref name="maxDistToColMm"/> of a column centroid, and whose
        /// vertices are all inside a parent slab polygon.
        /// Returns (SlabIndex, ColIndex, DropPolygon).
        /// </summary>
        public static List<(int SlabIndex, int ColIndex, List<(double X, double Y)> Polygon)>
            DetectDropPanels(
                IReadOnlyList<List<(double X, double Y)>> slabs,
                IReadOnlyList<(double X, double Y)> columnCentroids,
                IReadOnlyList<List<(double X, double Y)>> candidatePolygons,
                double minDiagMm = 300,
                double maxDiagMm = 2000,
                double maxDistToColMm = 3000)
        {
            var result = new List<(int, int, List<(double X, double Y)>)>();

            foreach (var poly in candidatePolygons)
            {
                if (poly.Count < 3) continue;

                double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X);
                double minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
                double diag = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
                if (diag < minDiagMm || diag > maxDiagMm) continue;

                double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0;

                int nearestCol = -1;
                double bestDist = maxDistToColMm;
                for (int ci = 0; ci < columnCentroids.Count; ci++)
                {
                    double d = Distance(columnCentroids[ci], (cx, cy));
                    if (d < bestDist) { bestDist = d; nearestCol = ci; }
                }
                if (nearestCol < 0) continue;

                int parentSlab = -1;
                for (int si = 0; si < slabs.Count; si++)
                {
                    var (dcx, dcy) = PolygonAreaCentroid(poly);
                    if (PointInPolygon((dcx, dcy), slabs[si]))
                    {
                        parentSlab = si;
                        break;
                    }
                }
                if (parentSlab < 0) continue;

                result.Add((parentSlab, nearestCol, poly));
            }
            return result;
        }

        /// <summary>
        /// Processes a collection of raw slab polygons: simplifies with Douglas-Peucker
        /// and normalizes to CCW winding order.
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
                var s = DouglasPeuckerClosed(slabs[i], epsilonMm);
                if (s.Count >= 3) slabs[i] = EnsureCCW(s);
                else { slabs.RemoveAt(i); colors.RemoveAt(i); }
            }

            // Deduplicate slabs whose vertices round to the same coordinates (within 1mm).
            // Multiple PDF paths can trace the same slab region; after D-P simplification
            // they collapse to identical or near-identical polygons.
            var seen = new HashSet<string>();
            for (int i = slabs.Count - 1; i >= 0; i--)
            {
                // Build canonical key: sort vertices by (X,Y) to handle different start points,
                // round to 1mm to catch near-duplicates from DP epsilon variation.
                var sorted = slabs[i].OrderBy(p => Math.Round(p.X)).ThenBy(p => Math.Round(p.Y));
                var key = string.Join("|", sorted.Select(p => $"{Math.Round(p.X)},{Math.Round(p.Y)}"));
                if (!seen.Add(key))
                {
                    slabs.RemoveAt(i);
                    colors.RemoveAt(i);
                }
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
