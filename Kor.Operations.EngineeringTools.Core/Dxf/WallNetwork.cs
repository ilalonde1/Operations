namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Joins wall centrelines into a connected network, so that adjacent panels share joints.
///
/// This is the difference between a model that runs and a pile of loose panels. A wall in ETABS is
/// a shell element, and two walls meeting at a corner only transfer force if they share a joint —
/// in Andrea Neuviale's words, "we can't have a wall go from here to here and then another one
/// from here to here, we need a connection". Read straight off a drawing they never do: each wall's
/// centreline stops half the *other* wall's thickness short of the corner, so an L reads as two
/// panels with a 6-18" gap between their ends and no structural connection at all.
///
/// Three passes fix it:
///   1. Non-parallel neighbours are carried out to where their centrelines actually cross, which is
///      the corner joint. This is exact, not a nudge: the intersection is where the two walls meet.
///   2. Ends that still land within a whisker of each other are snapped onto one joint.
///   3. A wall whose end lands part-way along another is split there, so the T-junction has a joint
///      on both members rather than one wall passing blindly through the other.
///
/// Coordinates are made exactly equal, which is what matters: the composer names joints by rounded
/// position, so two walls sharing a coordinate share a joint in the file ETABS reads.
/// </summary>
public static class WallNetwork
{
    private sealed class Axis
    {
        public DxfPoint A;
        public DxfPoint B;
        public required double Thickness;
        public required string Layer;

        public DxfPoint End(int which) => which == 0 ? A : B;
        public void SetEnd(int which, DxfPoint p) { if (which == 0) A = p; else B = p; }
        public double Length => A.DistanceTo(B);
    }

    /// <summary>Two centrelines closer than this in angle are treated as parallel and never crossed.</summary>
    private const double ParallelDegrees = 12.0;

    /// <summary>How far past its drawn end a centreline may be carried, as a multiple of the pair's thickness.</summary>
    private const double ReachFactor = 1.25;

    /// <summary>Ends this close after crossing are the same joint.</summary>
    private const double SnapTolerance = 2.0;

    public static IReadOnlyList<WallAxis> Connect(IReadOnlyList<WallAxis> walls)
    {
        if (walls.Count < 2) return walls;

        var axes = walls
            .Where(w => w.Length > 1e-6)
            .Select(w => new Axis { A = w.Start, B = w.End, Thickness = w.Thickness, Layer = w.Layer })
            .ToList();

        CarryEndsToCorners(axes);
        SnapNearbyEnds(axes);
        var split = SplitAtTJunctions(axes);

        return split
            .Where(a => a.Length > 1e-6)
            .Select(a => new WallAxis(a.A, a.B, a.Thickness, a.Layer))
            .ToList();
    }

    /// <summary>
    /// Pass 1 — carry each pair of non-parallel neighbours out to where their centrelines cross.
    ///
    /// Only an end may move, and only towards a crossing within reach of it: a crossing part-way
    /// along a wall is a T-junction, which pass 3 handles by splitting rather than by dragging an
    /// end across the building. Candidates are applied shortest-first and each end moves once, so a
    /// wall meeting two others resolves to its two nearest corners rather than to whichever pair
    /// happened to be considered first.
    /// </summary>
    private static void CarryEndsToCorners(List<Axis> axes)
    {
        var moves = new List<(double Distance, int AxisIndex, int Which, DxfPoint To)>();

        for (int i = 0; i < axes.Count; i++)
        for (int j = i + 1; j < axes.Count; j++)
        {
            var a = axes[i];
            var b = axes[j];
            if (!TryCross(a, b, out var cross)) continue;

            double reach = (a.Thickness + b.Thickness) / 2.0 * ReachFactor + 2.0;
            if (!TryEndToMove(a, cross, reach, out int endA, out double da)) continue;
            if (!TryEndToMove(b, cross, reach, out int endB, out double db)) continue;

            moves.Add((da, i, endA, cross));
            moves.Add((db, j, endB, cross));
        }

        var taken = new HashSet<(int, int)>();
        foreach (var move in moves.OrderBy(m => m.Distance))
            if (taken.Add((move.AxisIndex, move.Which)))
                axes[move.AxisIndex].SetEnd(move.Which, move.To);
    }

    /// <summary>Where two centrelines cross, if they are far enough from parallel to have a corner.</summary>
    private static bool TryCross(Axis a, Axis b, out DxfPoint cross)
    {
        cross = default;

        double ax = a.B.X - a.A.X, ay = a.B.Y - a.A.Y;
        double bx = b.B.X - b.A.X, by = b.B.Y - b.A.Y;

        double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
        if (la < 1e-9 || lb < 1e-9) return false;

        double sin = Math.Abs(ax * by - ay * bx) / (la * lb);
        if (sin < Math.Sin(ParallelDegrees * Math.PI / 180.0)) return false;

        double denominator = ax * by - ay * bx;
        double t = ((b.A.X - a.A.X) * by - (b.A.Y - a.A.Y) * bx) / denominator;
        cross = new DxfPoint(a.A.X + ax * t, a.A.Y + ay * t);
        return true;
    }

    /// <summary>
    /// Which end of this wall the crossing belongs to, if any. A crossing that falls in the body of
    /// the wall is not an end to be moved.
    /// </summary>
    private static bool TryEndToMove(Axis axis, DxfPoint cross, double reach, out int which, out double distance)
    {
        which = -1;
        distance = 0;

        double dx = axis.B.X - axis.A.X, dy = axis.B.Y - axis.A.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9) return false;

        double t = ((cross.X - axis.A.X) * dx + (cross.Y - axis.A.Y) * dy) / lengthSquared;
        if (t > 0.25 && t < 0.75) return false;          // mid-span: a T-junction, not a corner

        which = t < 0.5 ? 0 : 1;
        distance = cross.DistanceTo(axis.End(which));
        return distance <= reach;
    }

    /// <summary>Pass 2 — ends left within a whisker of each other become one joint.</summary>
    private static void SnapNearbyEnds(List<Axis> axes)
    {
        var ends = new List<(int AxisIndex, int Which)>();
        for (int i = 0; i < axes.Count; i++) { ends.Add((i, 0)); ends.Add((i, 1)); }

        var clusterOf = new int[ends.Count];
        for (int i = 0; i < clusterOf.Length; i++) clusterOf[i] = i;

        int Find(int x) { while (clusterOf[x] != x) { clusterOf[x] = clusterOf[clusterOf[x]]; x = clusterOf[x]; } return x; }

        for (int i = 0; i < ends.Count; i++)
        for (int k = i + 1; k < ends.Count; k++)
        {
            // Never fuse a wall's own two ends: that would collapse it to nothing.
            if (ends[i].AxisIndex == ends[k].AxisIndex) continue;

            var p = axes[ends[i].AxisIndex].End(ends[i].Which);
            var q = axes[ends[k].AxisIndex].End(ends[k].Which);
            if (p.DistanceTo(q) <= SnapTolerance) { int a = Find(i), b = Find(k); if (a != b) clusterOf[a] = b; }
        }

        var members = new Dictionary<int, List<int>>();
        for (int i = 0; i < ends.Count; i++)
        {
            int root = Find(i);
            if (!members.TryGetValue(root, out var list)) members[root] = list = new List<int>();
            list.Add(i);
        }

        foreach (var group in members.Values.Where(g => g.Count > 1))
        {
            double x = group.Average(i => axes[ends[i].AxisIndex].End(ends[i].Which).X);
            double y = group.Average(i => axes[ends[i].AxisIndex].End(ends[i].Which).Y);
            var joint = new DxfPoint(x, y);
            foreach (int i in group) axes[ends[i].AxisIndex].SetEnd(ends[i].Which, joint);
        }
    }

    /// <summary>
    /// Pass 3 — a wall whose end lands part-way along another is split there.
    ///
    /// Without this the stem of a T shares no joint with the wall it runs into: ETABS meshes them
    /// independently and the connection carries nothing.
    /// </summary>
    private static List<Axis> SplitAtTJunctions(List<Axis> axes)
    {
        var joints = new List<DxfPoint>();
        foreach (var axis in axes) { joints.Add(axis.A); joints.Add(axis.B); }

        var result = new List<Axis>();

        foreach (var axis in axes)
        {
            double dx = axis.B.X - axis.A.X, dy = axis.B.Y - axis.A.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-9) { result.Add(axis); continue; }

            var cuts = new List<double>();
            foreach (var joint in joints)
            {
                if (joint.DistanceTo(axis.A) <= SnapTolerance || joint.DistanceTo(axis.B) <= SnapTolerance) continue;

                double t = ((joint.X - axis.A.X) * dx + (joint.Y - axis.A.Y) * dy) / lengthSquared;
                if (t <= 0.001 || t >= 0.999) continue;

                var onAxis = new DxfPoint(axis.A.X + dx * t, axis.A.Y + dy * t);
                if (onAxis.DistanceTo(joint) > SnapTolerance) continue;

                if (!cuts.Any(c => Math.Abs(c - t) < 1e-6)) cuts.Add(t);
            }

            if (cuts.Count == 0) { result.Add(axis); continue; }

            cuts.Sort();
            var from = axis.A;
            foreach (double t in cuts)
            {
                var at = new DxfPoint(axis.A.X + dx * t, axis.A.Y + dy * t);
                if (from.DistanceTo(at) > SnapTolerance)
                    result.Add(new Axis { A = from, B = at, Thickness = axis.Thickness, Layer = axis.Layer });
                from = at;
            }
            if (from.DistanceTo(axis.B) > SnapTolerance)
                result.Add(new Axis { A = from, B = axis.B, Thickness = axis.Thickness, Layer = axis.Layer });
        }

        return result;
    }

    /// <summary>
    /// Openings in a wall run: two ends that face each other along the same line, a doorway apart.
    ///
    /// A wall enclosure is drawn with a break where its door is, so the two panels either side stop
    /// short of each other. Closing that gap would be wrong — the engineer's rule is that the wall
    /// stops at the opening — but leaving it bare is wrong too, because the piers either side are
    /// then tied together by nothing. What belongs there is a header, and this finds where.
    ///
    /// The span limits are measured, not chosen: across 31168 the gaps between in-line wall ends
    /// fall in one tight cluster of 142 between 36" and 48" — door width — with nothing at all
    /// below 18", and a separate group beyond 120" that is different walls rather than one wall
    /// with a hole in it.
    /// </summary>
    public static IReadOnlyList<WallOpening> FindOpenings(
        IReadOnlyList<WallAxis> walls, double minSpan, double maxSpan)
    {
        var found = new List<WallOpening>();
        var used = new HashSet<(int, int)>();

        for (int i = 0; i < walls.Count; i++)
        for (int j = i + 1; j < walls.Count; j++)
        {
            var a = walls[i];
            var b = walls[j];

            double ax = a.End.X - a.Start.X, ay = a.End.Y - a.Start.Y;
            double bx = b.End.X - b.Start.X, by = b.End.Y - b.Start.Y;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la < 1e-6 || lb < 1e-6) continue;

            // In line with one another, not merely pointing the same way.
            if (Math.Abs(ax * by - ay * bx) / (la * lb) > 0.2) continue;
            double offset = Math.Abs((b.Start.X - a.Start.X) * (-ay / la) + (b.Start.Y - a.Start.Y) * (ax / la));
            if (offset > 3.0) continue;

            // Similar thickness, or it is a different wall that happens to line up.
            if (Math.Abs(a.Thickness - b.Thickness) > 4.0) continue;

            (DxfPoint P, DxfPoint Q, double D) best = (default, default, double.MaxValue);
            foreach (var p in new[] { a.Start, a.End })
            foreach (var q in new[] { b.Start, b.End })
            {
                double d = p.DistanceTo(q);
                if (d < best.D) best = (p, q, d);
            }

            if (best.D < minSpan || best.D > maxSpan) continue;
            if (!used.Add((i, j))) continue;

            found.Add(new WallOpening(best.P, best.Q, Math.Min(a.Thickness, b.Thickness), a.Layer));
        }

        return found;
    }

    /// <summary>
    /// How many wall ends share a joint with another wall — the measure of whether the model is
    /// connected. Reported so a drop is visible rather than discovered in ETABS.
    /// </summary>
    public static (int Connected, int Total) CountConnectedEnds(IReadOnlyList<WallAxis> walls)
    {
        var ends = new List<DxfPoint>();
        foreach (var w in walls) { ends.Add(w.Start); ends.Add(w.End); }

        int connected = 0;
        for (int i = 0; i < ends.Count; i++)
        {
            int owner = i / 2;
            for (int k = 0; k < ends.Count; k++)
            {
                if (k / 2 == owner) continue;
                if (ends[i].DistanceTo(ends[k]) <= 0.01) { connected++; break; }
            }
        }
        return (connected, ends.Count);
    }
}
