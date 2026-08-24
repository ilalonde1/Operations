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
    /// One ring that crosses itself, as the separate rings it is actually drawing.
    ///
    /// A slab edge that closes through its own linework comes back as a figure of eight, and
    /// every measure that would catch it agrees it is fine: 31168's LEVEL 2 podium arrived as a
    /// 16-joint ring, 296 x 96 ft, sensible area, whose two wings met at (26, 248) ft. ETABS
    /// meshes that badly or refuses it, and an engineer opening the model sees an hourglass where
    /// two wings were drawn.
    ///
    /// Where two edges of the ring cross, the crossing point is where one wing ends and the next
    /// begins — so the split is the drawing's own answer, not a judgement about it. Each half is
    /// then checked again, because an outline can cross itself more than once.
    ///
    /// Rings that do not cross come back as themselves, so this is safe to run over everything.
    /// </summary>
    public static List<List<DxfPoint>> SplitSelfCrossings(IReadOnlyList<DxfPoint> ring, double touchTolerance = 0.05, int depth = 0)
    {
        var single = new List<List<DxfPoint>> { ring.ToList() };

        // A ring can only be split so many times before something is wrong with the ring rather
        // than with the outline; stopping is better than recurring forever on degenerate input.
        // Four is the smallest ring that can cross itself — a bowtie. A triangle cannot.
        if (ring.Count < 4 || depth > 16) return single;

        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            for (int k = i + 2; k < n; k++)
            {
                // Neighbouring edges share a joint, and the last edge neighbours the first.
                if (i == 0 && k == n - 1) continue;

                var hit = SelfTouch(ring[i], ring[(i + 1) % n], ring[k], ring[(k + 1) % n], touchTolerance);
                if (hit is null) continue;

                // One lobe is everything between the two touching edges; the other is the rest.
                var lobeA = new List<DxfPoint> { hit.Value };
                for (int j = i + 1; j <= k; j++) lobeA.Add(ring[j]);

                var lobeB = new List<DxfPoint> { hit.Value };
                for (int j = k + 1; j < n; j++) lobeB.Add(ring[j]);
                for (int j = 0; j <= i; j++) lobeB.Add(ring[j]);

                // Where the touch is AT a vertex rather than mid-edge, that vertex is the touch
                // point, so it would otherwise appear twice running.
                lobeA = WithoutRepeats(lobeA, touchTolerance);
                lobeB = WithoutRepeats(lobeB, touchTolerance);

                // A lobe with no area is a SPUR, not a wing: the outline doubled back along itself
                // for a couple of inches and came straight out again. 31168's LEVEL 2 has one at
                // the waist, two joints 2 inches apart. Splitting there produces nothing, and the
                // first version of this simply gave up when it saw that — leaving a plate that
                // still met itself at 0.00 after the real hourglass had been cut in two.
                //
                // Drop the spur and carry on with the ring that has substance.
                bool aReal = lobeA.Count >= 3, bReal = lobeB.Count >= 3;
                if (!aReal && !bReal) continue;

                if (!aReal || !bReal)
                {
                    var kept = aReal ? lobeA : lobeB;
                    // Only if trimming actually shortened it, or this recurs forever.
                    if (kept.Count >= n) continue;
                    return SplitSelfCrossings(kept, touchTolerance, depth + 1);
                }

                var split = new List<List<DxfPoint>>();
                split.AddRange(SplitSelfCrossings(lobeA, touchTolerance, depth + 1));
                split.AddRange(SplitSelfCrossings(lobeB, touchTolerance, depth + 1));
                return split;
            }
        }

        return single;
    }

    /// <summary>
    /// Where a ring meets its own edge, or null when these two edges miss each other.
    ///
    /// Both shapes count and 31168 turned out to be the second one. An X-crossing has the
    /// intersection strictly inside both edges. A T-touch has a VERTEX sitting on another edge:
    /// LEVEL 2's outline meets itself at u = 1.0000000113 — the end of one edge landing exactly
    /// on another, gap 0.0000 in. Requiring a strict crossing found nothing and the hourglass
    /// survived a republish unchanged, which is how this was caught.
    /// </summary>
    private static DxfPoint? SelfTouch(DxfPoint a1, DxfPoint a2, DxfPoint b1, DxfPoint b2, double tolerance)
    {
        double rx = a2.X - a1.X, ry = a2.Y - a1.Y;
        double sx = b2.X - b1.X, sy = b2.Y - b1.Y;
        double denom = rx * sy - ry * sx;

        if (Math.Abs(denom) > 1e-9)
        {
            double t = ((b1.X - a1.X) * sy - (b1.Y - a1.Y) * sx) / denom;
            double u = ((b1.X - a1.X) * ry - (b1.Y - a1.Y) * rx) / denom;

            // Inside both, allowing an endpoint to count: that is the T-touch. Adjacent edges
            // share a joint by construction and are excluded before this is ever called, so an
            // endpoint meeting here is the ring genuinely reaching back onto itself.
            const double Slack = 1e-6;
            if (t >= -Slack && t <= 1 + Slack && u >= -Slack && u <= 1 + Slack)
                return new DxfPoint(a1.X + t * rx, a1.Y + t * ry);
        }

        // Parallel, or numerically awkward: fall back to distance. An endpoint lying on the other
        // edge within tolerance is the same self-touch by another route.
        foreach (var (p, c1, c2) in new[] { (a1, b1, b2), (a2, b1, b2), (b1, a1, a2), (b2, a1, a2) })
        {
            var (dist, at) = NearestOn(p, c1, c2);
            if (dist <= tolerance) return at;
        }

        return null;
    }

    private static (double Distance, DxfPoint At) NearestOn(DxfPoint p, DxfPoint a, DxfPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = dx * dx + dy * dy;
        double t = len <= 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
        var at = new DxfPoint(a.X + t * dx, a.Y + t * dy);
        return (p.DistanceTo(at), at);
    }

    /// <summary>Consecutive points at the same place, collapsed — including across the ring's close.</summary>
    private static List<DxfPoint> WithoutRepeats(List<DxfPoint> ring, double tolerance)
    {
        var kept = new List<DxfPoint>();
        foreach (var p in ring)
            if (kept.Count == 0 || kept[^1].DistanceTo(p) > tolerance) kept.Add(p);
        while (kept.Count > 1 && kept[0].DistanceTo(kept[^1]) <= tolerance) kept.RemoveAt(kept.Count - 1);
        return kept;
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
