#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// A FILLED SHAPE MAY ARRIVE AS TWO TRIANGLES (intake step 61, 2026-09-14). A PDF driver tessellates
    /// a filled rectangle into two triangles on its diagonal; the page then holds two filled three-point
    /// paths of one colour sharing one edge, and nothing four-cornered. Step 58 read a filled three-point
    /// shape as a symbol's triangle and run 9 over the corpus lost 47,963 columns on 144 sets that the
    /// harness, drawn with four corners, never showed (31048-01: 2,640 columns to 57 against 449 of the
    /// engineer's). Two filled triangles of one colour that share an edge (two vertices within a hair)
    /// and whose union is a convex quadrilateral are one shape: the first, in page order, carries the four
    /// corners; the second is its other half.
    /// </summary>
    /// <remarks>
    /// WHAT THIS COVERS: a rectangle or any convex quadrilateral drawn as two triangles on a diagonal, in
    /// either winding, with the shared edge's vertices equal to a hundredth of a millimetre. WHAT IT DOES
    /// NOT: a shape drawn as three or more triangles (a fan); two triangles that meet at a vertex only; a
    /// concave union (a chevron) — those stay three-point shapes and are judged as such; triangles of two
    /// colours.
    /// </remarks>
    public static class TriangleTwins
    {
        public sealed record Pairing(IReadOnlyDictionary<int, List<(double X, double Y)>> Union, IReadOnlyDictionary<int, int> OtherHalfOf);

        private const double SameVertexMm = 0.01;

        public static Pairing Pair(IReadOnlyList<RawSubpath> subpaths)
        {
            ArgumentNullException.ThrowIfNull(subpaths);
            var union = new Dictionary<int, List<(double X, double Y)>>();
            var otherHalfOf = new Dictionary<int, int>();
            // candidates: filled, closed, three distinct corners; keyed by a rounded vertex so a twin is found among neighbours, not the page
            var byVertex = new Dictionary<(long, long), List<int>>();
            var corners = new Dictionary<int, List<(double X, double Y)>>();
            for (int i = 0; i < subpaths.Count; i++)
            {
                var s = subpaths[i];
                if (!s.IsFilled || !s.IsClosed || s.IsAnnotation || s.IsClipping) continue;
                var c = Distinct(s.Points);
                if (c.Count != 3) continue;
                corners[i] = c;
                foreach (var v in c)
                {
                    var key = ((long)Math.Round(v.X), (long)Math.Round(v.Y));
                    if (!byVertex.TryGetValue(key, out var list)) byVertex[key] = list = [];
                    list.Add(i);
                }
            }
            foreach (int i in corners.Keys.OrderBy(k => k))
            {
                if (otherHalfOf.ContainsKey(i) || union.ContainsKey(i)) continue;
                var mine = corners[i];
                // the twin: a later candidate of the same colour sharing exactly two vertices, the union convex
                int twin = -1;
                foreach (var v in mine)
                {
                    var key = ((long)Math.Round(v.X), (long)Math.Round(v.Y));
                    foreach (int j in byVertex.TryGetValue(key, out var near) ? near : [])
                    {
                        if (j <= i || otherHalfOf.ContainsKey(j) || union.ContainsKey(j) || subpaths[j].Color != subpaths[i].Color) continue;
                        var theirs = corners[j];
                        var shared = mine.Where(a => theirs.Any(b => Near(a, b))).ToList();
                        if (shared.Count != 2) continue;
                        var apexMine = mine.Single(a => !shared.Any(sh => Near(a, sh)));
                        var apexTheirs = theirs.Single(b => !shared.Any(sh => Near(b, sh)));
                        // the two apexes must lie on opposite sides of the shared edge, and the quadrilateral must be convex
                        double side1 = Cross(shared[0], shared[1], apexMine), side2 = Cross(shared[0], shared[1], apexTheirs);
                        if (side1 * side2 >= 0) continue;
                        var quad = new List<(double X, double Y)> { shared[0], apexMine, shared[1], apexTheirs };
                        if (!IsConvex(quad)) continue;
                        twin = j;
                        union[i] = quad;
                        break;
                    }
                    if (twin >= 0) break;
                }
                if (twin >= 0) otherHalfOf[twin] = i;
            }
            return new Pairing(union, otherHalfOf);
        }

        private static bool Near((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) <= SameVertexMm && Math.Abs(a.Y - b.Y) <= SameVertexMm;

        private static double Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

        private static List<(double X, double Y)> Distinct(List<(double X, double Y)> pts)
        {
            var d = new List<(double X, double Y)>();
            foreach (var p in pts) if (!d.Any(q => Near(p, q))) d.Add(p);
            return d;
        }

        private static bool IsConvex(List<(double X, double Y)> q)
        {
            int sign = 0;
            for (int i = 0; i < q.Count; i++)
            {
                double c = Cross(q[i], q[(i + 1) % q.Count], q[(i + 2) % q.Count]);
                if (Math.Abs(c) < 1e-9) return false;
                int s = c > 0 ? 1 : -1;
                if (sign == 0) sign = s; else if (s != sign) return false;
            }
            return true;
        }
    }
}
