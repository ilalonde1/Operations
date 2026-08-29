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

    /// <summary>
    /// Straightens a traced outline: Ramer-Douglas-Peucker over a closed ring.
    ///
    /// <see cref="Simplify"/> asks of each vertex whether it lies on the line between its two
    /// NEIGHBOURS, and that is the wrong question for a raster trace. Every step of a staircase
    /// deviates from its neighbours by a pixel, so every one survives, and a floor recovered by
    /// flood-filling arrives as 1,942 vertices where the drawing has perhaps forty. ETABS is
    /// handed the staircase and meshes it.
    ///
    /// This asks instead whether a whole RUN of vertices lies within tolerance of the straight
    /// line across it, which is what turns a thousand pixel steps back into one wall.
    ///
    /// The ring is split at its two most distant points before recursing: an open polyline has
    /// ends to anchor on and a closed ring does not, and anchoring at an arbitrary vertex bends
    /// the outline around it.
    /// </summary>
    public static List<DxfPoint> Straighten(IReadOnlyList<DxfPoint> ring, double tolerance)
    {
        if (ring.Count < 8 || tolerance <= 0) return ring.ToList();

        int a = 0, b = 0;
        double worst = -1;
        for (int i = 0; i < ring.Count; i++)
            for (int j = i + 1; j < ring.Count; j++)
            {
                double d = ring[i].DistanceTo(ring[j]);
                if (d > worst) { worst = d; a = i; b = j; }
            }

        var first = new List<DxfPoint>();
        for (int i = a; i != b; i = (i + 1) % ring.Count) first.Add(ring[i]);
        first.Add(ring[b]);

        var second = new List<DxfPoint>();
        for (int i = b; i != a; i = (i + 1) % ring.Count) second.Add(ring[i]);
        second.Add(ring[a]);

        var kept = Reduce(first, tolerance);
        kept.RemoveAt(kept.Count - 1);
        var back = Reduce(second, tolerance);
        back.RemoveAt(back.Count - 1);
        kept.AddRange(back);

        return kept.Count >= 3 ? kept : ring.ToList();
    }

    private static List<DxfPoint> Reduce(List<DxfPoint> run, double tolerance)
    {
        if (run.Count < 3) return run.ToList();

        double worst = 0;
        int at = 0;
        for (int i = 1; i < run.Count - 1; i++)
        {
            double d = PerpendicularDistance(run[i], run[0], run[^1]);
            if (d > worst) { worst = d; at = i; }
        }

        if (worst <= tolerance) return new List<DxfPoint> { run[0], run[^1] };

        var left = Reduce(run.GetRange(0, at + 1), tolerance);
        var right = Reduce(run.GetRange(at, run.Count - at), tolerance);
        left.RemoveAt(left.Count - 1);
        left.AddRange(right);
        return left;
    }

    public static double PerpendicularDistance(DxfPoint p, DxfPoint a, DxfPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return p.DistanceTo(a);
        return Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / len;
    }

    /// <summary>
    /// Removes vertices where an outline doubles back along itself.
    ///
    /// ETABS refuses these outright — "Area Object KF54 not correctly defined", followed by
    /// "Error reading line 12726. Line Ignored." for its assign, so the floor is silently absent
    /// from the model an engineer opens. Found by importing, which is the only thing that could
    /// have found it: the ring has the right area, the right position, no coincident points and no
    /// proper self-crossing. Three of its joints simply ran down 24 inches along one x and back up
    /// 96 along the same one.
    ///
    /// A spur is a vertex whose two edges point in OPPOSITE directions. The polygon has no width
    /// there, so nothing is lost by dropping it; what is gained is a plate ETABS will read.
    /// Iterated, because removing one spur can expose the next behind it.
    /// </summary>
    public static List<DxfPoint> RemoveSpurs(IReadOnlyList<DxfPoint> ring, double tolerance = 1e-6)
    {
        var pts = ring.ToList();

        for (bool again = true; again && pts.Count > 3; )
        {
            again = false;

            for (int i = 0; i < pts.Count && pts.Count > 3; i++)
            {
                var prev = pts[(i - 1 + pts.Count) % pts.Count];
                var cur = pts[i];
                var next = pts[(i + 1) % pts.Count];

                double ax = cur.X - prev.X, ay = cur.Y - prev.Y;
                double bx = next.X - cur.X, by = next.Y - cur.Y;

                double la = Math.Sqrt(ax * ax + ay * ay);
                double lb = Math.Sqrt(bx * bx + by * by);
                if (la < tolerance || lb < tolerance) { pts.RemoveAt(i); again = true; i--; continue; }

                // cos of the turn: -1 is a full reversal.
                double cos = (ax * bx + ay * by) / (la * lb);
                if (cos <= -1.0 + 1e-9)
                {
                    pts.RemoveAt(i);
                    again = true;
                    i--;
                }
            }
        }

        return pts;
    }

    /// <summary>True where an outline doubles back along itself at any vertex.</summary>
    public static bool HasSpur(IReadOnlyList<DxfPoint> ring)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            var prev = ring[(i - 1 + ring.Count) % ring.Count];
            var cur = ring[i];
            var next = ring[(i + 1) % ring.Count];

            double ax = cur.X - prev.X, ay = cur.Y - prev.Y;
            double bx = next.X - cur.X, by = next.Y - cur.Y;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la < 1e-9 || lb < 1e-9) return true;

            if ((ax * bx + ay * by) / (la * lb) <= -1.0 + 1e-9) return true;
        }

        return false;
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

    /// <summary>
    /// Does this ring pinch to less than <paramref name="minWidth"/> somewhere along its length?
    ///
    /// A floor traced from linework the drawing leaves open can come back with a SLOT through it:
    /// where the drawn edge is interrupted, the exterior flood reaches in through the gap and
    /// hollows the plate out from inside, and the channel it came in by survives as a slit in the
    /// outline. 31168's third mezzanine slab traced that way at 890 sq ft with an eighteen-inch
    /// slot running nine feet into it — an area that is right, a position that is right, and a
    /// diaphragm cut nearly in two at the stair. Every other test passes it: the ring does not
    /// cross itself, has no coincident points, and its shortest edge is five feet.
    ///
    /// A SLOT IS TWO EDGES FACING EACH OTHER; A STEP IS TWO EDGES FACING THE SAME WAY. That is the
    /// whole of it. Walking a staircase of small steps puts parallel edges close together too, and
    /// those run in the SAME direction with slab between them. The two sides of a slit run in
    /// OPPOSITE directions with nothing between them.
    /// </summary>
    public static bool HasNarrowNeck(IReadOnlyList<DxfPoint> ring, double minWidth)
    {
        int n = ring.Count;
        if (n < 4 || minWidth <= 0) return false;

        for (int i = 0; i < n; i++)
        {
            DxfPoint a1 = ring[i], a2 = ring[(i + 1) % n];
            double ax = a2.X - a1.X, ay = a2.Y - a1.Y;
            double alen = Math.Sqrt(ax * ax + ay * ay);
            if (alen < 1e-9) continue;

            for (int j = i + 2; j < n; j++)
            {
                if ((j + 1) % n == i) continue;             // adjacent round the back
                DxfPoint b1 = ring[j], b2 = ring[(j + 1) % n];
                double bx = b2.X - b1.X, by = b2.Y - b1.Y;
                double blen = Math.Sqrt(bx * bx + by * by);
                if (blen < 1e-9) continue;

                // Facing each other: near-parallel and running opposite ways.
                double dot = (ax * bx + ay * by) / (alen * blen);
                if (dot > -0.9) continue;

                if (SegmentDistance(a1, a2, b1, b2) < minWidth) return true;
            }
        }

        return false;

        static double SegmentDistance(DxfPoint a1, DxfPoint a2, DxfPoint b1, DxfPoint b2)
            => Math.Min(
                Math.Min(PointToSegment(a1, b1, b2), PointToSegment(a2, b1, b2)),
                Math.Min(PointToSegment(b1, a1, a2), PointToSegment(b2, a1, a2)));

        static double PointToSegment(DxfPoint p, DxfPoint a, DxfPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-9) return p.DistanceTo(a);
            double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0.0, 1.0);
            return p.DistanceTo(new DxfPoint(a.X + t * dx, a.Y + t * dy));
        }
    }

    /// <summary>
    /// Does this ring cross itself? A polygon whose edges cross is not an outline of anything, and
    /// ETABS will not open one: KOR's KF7 reached the engineer's screen as a 37-point self-crossing
    /// ring and came back as "Area Object KF7 not correctly defined".
    ///
    /// The composer has always applied this to OPENINGS. It was never applied to floor plates,
    /// which is how KF7 shipped, so it lives here now and both callers use the one implementation.
    /// Adjacent edges share a vertex; touching there is not crossing.
    /// </summary>
    public static bool SelfIntersects(IReadOnlyList<DxfPoint> polygon)
    {
        int n = polygon.Count;
        if (n < 4) return false;

        for (int i = 0; i < n; i++)
        {
            DxfPoint a1 = polygon[i], a2 = polygon[(i + 1) % n];
            for (int j = i + 1; j < n; j++)
            {
                if ((j + 1) % n == i || j == (i + 1) % n) continue;
                if (Crosses(a1, a2, polygon[j], polygon[(j + 1) % n])) return true;
            }
        }

        return false;

        static bool Crosses(DxfPoint p1, DxfPoint p2, DxfPoint q1, DxfPoint q2)
        {
            double d1 = Side(q1, q2, p1), d2 = Side(q1, q2, p2);
            double d3 = Side(p1, p2, q1), d4 = Side(p1, p2, q2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        static double Side(DxfPoint a, DxfPoint b, DxfPoint c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }
}
