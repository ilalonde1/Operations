namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>Planar geometry helpers used to turn drawn outlines into structural members.</summary>
public static class LoopGeometry
{
    /// <summary>
    /// The rectangle of least area containing the loop, found by testing every edge
    /// direction as a candidate axis. Exact for rectangles, which is what wall and
    /// column footprints are, and stable for the near-rectangles drafting produces.
    /// </summary>
    public static OrientedBox MinAreaBox(IReadOnlyList<DxfPoint> points)
    {
        if (points.Count < 2)
        {
            var p = points.Count == 1 ? points[0] : new DxfPoint(0, 0);
            return new OrientedBox(p, p, p, 0, 0);
        }

        double bestArea = double.MaxValue;
        OrientedBox best = default!;

        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;

            double ux = dx / len, uy = dy / len;   // axis
            double vx = -uy, vy = ux;              // normal

            double minU = double.MaxValue, maxU = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;
            foreach (var p in points)
            {
                double u = p.X * ux + p.Y * uy;
                double v = p.X * vx + p.Y * vy;
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            double du = maxU - minU, dv = maxV - minV;
            double area = du * dv;
            if (area >= bestArea) continue;

            bestArea = area;

            // Long direction wins as the member axis; the other extent is its thickness.
            double midV = (minV + maxV) / 2.0, midU = (minU + maxU) / 2.0;
            DxfPoint FromUv(double u, double v) => new(u * ux + v * vx, u * uy + v * vy);

            best = du >= dv
                ? new OrientedBox(FromUv(midU, midV), FromUv(minU, midV), FromUv(maxU, midV), du, dv)
                : new OrientedBox(FromUv(midU, midV), FromUv(midU, minV), FromUv(midU, maxV), dv, du);
        }

        if (best is null)
        {
            var p = points[0];
            return new OrientedBox(p, p, p, 0, 0);
        }
        return best;
    }

    public static bool PointInPolygon(DxfPoint test, IReadOnlyList<DxfPoint> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            if (pi.Y > test.Y != pj.Y > test.Y &&
                test.X < (pj.X - pi.X) * (test.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>Drops collinear and near-duplicate vertices so ETABS gets clean polygons.</summary>
    public static List<DxfPoint> Simplify(IReadOnlyList<DxfPoint> points, double tolerance)
    {
        var kept = new List<DxfPoint>();
        foreach (var p in points)
            if (kept.Count == 0 || kept[^1].DistanceTo(p) > tolerance)
                kept.Add(p);

        if (kept.Count > 2 && kept[0].DistanceTo(kept[^1]) <= tolerance)
            kept.RemoveAt(kept.Count - 1);

        if (kept.Count < 4) return kept;

        var result = new List<DxfPoint>();
        for (int i = 0; i < kept.Count; i++)
        {
            var prev = kept[(i - 1 + kept.Count) % kept.Count];
            var cur = kept[i];
            var next = kept[(i + 1) % kept.Count];

            double cross = (cur.X - prev.X) * (next.Y - prev.Y) - (cur.Y - prev.Y) * (next.X - prev.X);
            double scale = Math.Max(prev.DistanceTo(next), 1e-9);
            if (Math.Abs(cross) / scale > tolerance) result.Add(cur);
        }

        return result.Count >= 3 ? result : kept;
    }

    /// <summary>
    /// Whether two segments touch — crossing, meeting end to end, or running into one another's
    /// middle. Used to decide whether a short wall is joined to a core or standing on its own,
    /// which is the difference between a return wall and a column.
    /// </summary>
    public static bool SegmentsMeet(DxfPoint a1, DxfPoint a2, DxfPoint b1, DxfPoint b2, double tolerance)
    {
        static double DistanceToSegment(DxfPoint p, DxfPoint a, DxfPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-9) return p.DistanceTo(a);
            double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0.0, 1.0);
            return p.DistanceTo(new DxfPoint(a.X + t * dx, a.Y + t * dy));
        }

        // A proper crossing: each segment straddles the other's line.
        double d1 = (a2.X - a1.X) * (b1.Y - a1.Y) - (a2.Y - a1.Y) * (b1.X - a1.X);
        double d2 = (a2.X - a1.X) * (b2.Y - a1.Y) - (a2.Y - a1.Y) * (b2.X - a1.X);
        double d3 = (b2.X - b1.X) * (a1.Y - b1.Y) - (b2.Y - b1.Y) * (a1.X - b1.X);
        double d4 = (b2.X - b1.X) * (a2.Y - b1.Y) - (b2.Y - b1.Y) * (a2.X - b1.X);
        if (Math.Sign(d1) != Math.Sign(d2) && Math.Sign(d3) != Math.Sign(d4)) return true;

        // Or an end of one lands on the other, within tolerance.
        return DistanceToSegment(a1, b1, b2) <= tolerance
            || DistanceToSegment(a2, b1, b2) <= tolerance
            || DistanceToSegment(b1, a1, a2) <= tolerance
            || DistanceToSegment(b2, a1, a2) <= tolerance;
    }
}
