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
///
/// ⚠ EVERY LENGTH HERE IS IN INCHES AND IS SCALED BY <c>inch</c>, THE DRAWING UNITS IN ONE INCH (2026-10-27).
/// The classifier runs in the model's unit, and these literals were applied in it as they stood: in a feet model the
/// 2 in snap was 2 ft, so Halifax LEVEL 1's corner ends 329 mm apart were averaged into a tilted joint, and the 2 in
/// of reach became 2 ft and carried a wall end 1145 mm across its own 969 mm door; in a millimetre model - every PDF-route
/// build - the same 2 in was 2 mm, and the door finder's "within 4 in of the same thickness" was 4 mm.
/// AModelIsTheSameInInchesAndMillimetresTests composes the Halifax core in all three units and diffs them.
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

    /// <summary>Ends this close after crossing are the same joint, in inches.</summary>
    internal const double SnapTolerance = 2.0;   // internal: WallNetworkInvariant allows a lateral shift up to it

    /// <param name="walls">The centrelines, in the drawing's unit.</param>
    /// <param name="inch">How many of the drawing's units make an inch: 1 in an inch drawing, 25.4 in millimetres, 1/12 in feet.</param>
    public static IReadOnlyList<WallAxis> Connect(IReadOnlyList<WallAxis> walls, double inch = 1.0)
    {
        if (walls.Count < 2) return walls;

        var axes = walls
            .Where(w => w.Length > 1e-6)
            .Select(w => new Axis { A = w.Start, B = w.End, Thickness = w.Thickness, Layer = w.Layer })
            .ToList();

        double snap = SnapTolerance * inch;
        CarryEndsToCorners(axes, inch);
        SnapNearbyEnds(axes, snap);
        var split = SplitAtTJunctions(axes, snap);

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
    private static void CarryEndsToCorners(List<Axis> axes, double inch)
    {
        var moves = new List<(double Distance, int AxisIndex, int Which, DxfPoint To)>();

        for (int i = 0; i < axes.Count; i++)
        for (int j = i + 1; j < axes.Count; j++)
        {
            var a = axes[i];
            var b = axes[j];
            if (!TryCross(a, b, out var cross)) continue;

            double reach = (a.Thickness + b.Thickness) / 2.0 * ReachFactor + 2.0 * inch;
            if (!TryEndToMove(a, cross, reach, b.Thickness, out int endA, out double da)) continue;
            if (!TryEndToMove(b, cross, reach, a.Thickness, out int endB, out double db)) continue;

            moves.Add((da, i, endA, cross));
            moves.Add((db, j, endB, cross));
        }

        // Shortest first, and a TIE is broken by the walls' order, not by the arithmetic: two equal stubs either
        // side of a pier reach its axis at the same distance to the micron, and which "won" the sort was the
        // rounding noise of the frame the sheet happened to be drawn in (intake step 56, 2026-09-13).
        var taken = new HashSet<(int, int)>();
        foreach (var move in moves.OrderBy(m => Math.Round(m.Distance, 6)).ThenBy(m => m.AxisIndex).ThenBy(m => m.Which))
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
    ///
    /// "The body" was the middle half of the wall's length, and a SHORT wall's corner falls in it. Halifax LEVEL 1's
    /// corner piece is 1027 mm long and runs on to the far face of the 24 in wall it turns into, so the centrelines cross
    /// 275 mm in from its end - t = 0.27 - and the L was never joined: both pieces stood alone, under 48 in, and became
    /// columns (2026-10-27). A crossing inside the wall is a corner when what lies beyond it is no more than the other
    /// wall's half thickness, with the same reach factor: the wall stops at the other's far face. A T is a wall running on.
    /// </summary>
    private static bool TryEndToMove(Axis axis, DxfPoint cross, double reach, double otherThickness, out int which, out double distance)
    {
        which = -1;
        distance = 0;

        double dx = axis.B.X - axis.A.X, dy = axis.B.Y - axis.A.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9) return false;

        double t = ((cross.X - axis.A.X) * dx + (cross.Y - axis.A.Y) * dy) / lengthSquared;
        which = t < 0.5 ? 0 : 1;
        distance = cross.DistanceTo(axis.End(which));
        if (t > 0.25 && t < 0.75 && LoopGeometry.Beyond(distance, otherThickness / 2.0 * ReachFactor))
            return false;                                // mid-span: a T-junction, not a corner
        return LoopGeometry.Within(distance, reach);
    }

    /// <summary>Pass 2 — ends left within a whisker of each other become one joint.</summary>
    private static void SnapNearbyEnds(List<Axis> axes, double snap)
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

            // NOR A WALL'S ENDS WITH A COPY OF ITSELF (2026-10-27). An outline drawn twice ~25 mm apart reads as two
            // parallel axes; fused end to end they "join", so neither is a standalone stub and 31087's column shipped as a
            // 762 mm wall once the snap was the 2 in it was written for. Left apart, both are what they were at 2 mm.
            if (NearCopies(axes[ends[i].AxisIndex], axes[ends[k].AxisIndex])) continue;

            var p = axes[ends[i].AxisIndex].End(ends[i].Which);
            var q = axes[ends[k].AxisIndex].End(ends[k].Which);
            if (LoopGeometry.Within(p.DistanceTo(q), snap)) { int a = Find(i), b = Find(k); if (a != b) clusterOf[a] = b; }
        }

        var members = new Dictionary<int, List<int>>();
        for (int i = 0; i < ends.Count; i++)
        {
            int root = Find(i);
            if (!members.TryGetValue(root, out var list)) members[root] = list = new List<int>();
            list.Add(i);
        }

        // A JOINT MOVES AN END ALONG ITS WALL, NOT ACROSS IT (2026-10-27, WallNetworkInvariant). The joint was the plain
        // average of the ends, so two walls whose centrelines run ~30 mm apart were each pulled half-way across: at a 2 in
        // snap that tilted 79 walls on the metric gate sets, and at 2 ft Halifax's four corner pieces. The joint is now
        // weighted by length - a long wall hardly moves - and an end that would turn its wall more than
        // WallNetworkInvariant.ToleranceDegrees to reach it stays where it is, a joint of its own.
        foreach (var group in members.Values.Where(g => g.Count > 1))
        {
            var kept = group.ToList();
            DxfPoint joint = default;
            while (kept.Count > 1)
            {
                double weight = kept.Sum(i => Math.Max(axes[ends[i].AxisIndex].Length, 1e-9));
                joint = new DxfPoint(
                    kept.Sum(i => axes[ends[i].AxisIndex].End(ends[i].Which).X * Math.Max(axes[ends[i].AxisIndex].Length, 1e-9)) / weight,
                    kept.Sum(i => axes[ends[i].AxisIndex].End(ends[i].Which).Y * Math.Max(axes[ends[i].AxisIndex].Length, 1e-9)) / weight);
                var turned = kept.Where(i => Turn(axes[ends[i].AxisIndex], ends[i].Which, joint) > WallNetworkInvariant.ToleranceDegrees).ToList();
                if (turned.Count == 0) break;
                kept.Remove(turned.MaxBy(i => Turn(axes[ends[i].AxisIndex], ends[i].Which, joint)));
            }
            if (kept.Count < 2) continue;
            foreach (int i in kept) axes[ends[i].AxisIndex].SetEnd(ends[i].Which, joint);
        }
    }

    /// <summary>How far, in degrees, moving this end of the axis to <paramref name="to"/> turns the axis.</summary>
    private static double Turn(Axis axis, int which, DxfPoint to)
    {
        var fixedEnd = axis.End(1 - which);
        double before = Math.Atan2(axis.End(which).Y - fixedEnd.Y, axis.End(which).X - fixedEnd.X);
        double after = Math.Atan2(to.Y - fixedEnd.Y, to.X - fixedEnd.X);
        double d = Math.Abs(before - after) * 180.0 / Math.PI;
        return Math.Min(d, 360.0 - d);
    }

    /// <summary>
    /// A WALL BROKEN FOR A COLUMN RUNS THROUGH IT (2026-10-27, board 52; VD's decision). A drawing stops a wall at each face of a
    /// column standing in it; the opening finder then read the two pieces as jambs and spanned a header across the column - 84
    /// of 3,177 headers on the 57 gate sets, 79 of 2,833 before tonight's network work. The engineer's model runs the wall
    /// through (30972 P3; Jim on Halifax's south-west wall: "one continuous 8 in wall with the 12x30 columns in it"). Two pieces
    /// join when they are parallel within <see cref="WallNetworkInvariant.ToleranceDegrees"/>, in line within 3 in, of
    /// thickness within 4 in, and a column's centre lies between their facing ends within 300 mm of the line, the gap no wider
    /// than the column's extent along the wall plus 6 in (ASSUMED slack). The joined wall runs far end to far end on the longer
    /// piece's line, so it turns nothing; joins repeat until none is left (wall, column, wall, column, wall).
    /// </summary>
    /// <param name="inch">How many of the drawing's units make an inch.</param>
    public static List<WallAxis> JoinThroughColumns(IReadOnlyList<WallAxis> walls, IReadOnlyList<ColumnFootprint> columns, double inch, out int joined)
    {
        var list = walls.ToList();
        joined = 0;
        if (columns.Count == 0) return list;
        double reach = 300.0 / 25.4 * inch, slack = 6.0 * inch, inLine = 3.0 * inch, thick = 4.0 * inch;
        while (true)
        {
            (int I, int J, WallAxis Merged, double Gap) best = (-1, -1, null!, double.MaxValue);
            for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i]; var b = list[j];
                if (a.Length < 1e-9 || b.Length < 1e-9) continue;
                if (Math.Abs(a.Thickness - b.Thickness) > thick) continue;
                double ax = (a.End.X - a.Start.X) / a.Length, ay = (a.End.Y - a.Start.Y) / a.Length;
                double bx = (b.End.X - b.Start.X) / b.Length, by = (b.End.Y - b.Start.Y) / b.Length;
                if (Math.Asin(Math.Min(1.0, Math.Abs(ax * by - ay * bx))) * 180.0 / Math.PI > WallNetworkInvariant.ToleranceDegrees) continue;
                var (lng, sht) = a.Length >= b.Length ? (a, b) : (b, a);
                double ux = (lng.End.X - lng.Start.X) / lng.Length, uy = (lng.End.Y - lng.Start.Y) / lng.Length;
                double Along(DxfPoint p) => (p.X - lng.Start.X) * ux + (p.Y - lng.Start.Y) * uy;
                double Across(DxfPoint p) => Math.Abs((p.X - lng.Start.X) * -uy + (p.Y - lng.Start.Y) * ux);
                if (Across(sht.Start) > inLine || Across(sht.End) > inLine) continue;
                double s0 = Math.Min(Along(sht.Start), Along(sht.End)), s1 = Math.Max(Along(sht.Start), Along(sht.End));
                double gapFrom, gapTo;
                if (s0 >= lng.Length) { gapFrom = lng.Length; gapTo = s0; }
                else if (s1 <= 0) { gapFrom = s1; gapTo = 0; }
                else continue;                                   // overlapping pieces are not a broken run
                double gap = gapTo - gapFrom;
                if (gap <= 1e-9) continue;
                bool holdsColumn = columns.Any(c =>
                {
                    double t = Along(c.Center);
                    if (t <= gapFrom || t >= gapTo || Across(c.Center) > reach) return false;
                    double r = (c.AxisAngleDegrees - Math.Atan2(uy, ux) * 180.0 / Math.PI) * Math.PI / 180.0;
                    double extent = Math.Abs(c.Depth * Math.Cos(r)) + Math.Abs(c.Width * Math.Sin(r));   // Depth runs along the column's axis
                    return gap <= Math.Max(c.Width, c.Depth) + slack && gap <= extent + slack;
                });
                if (!holdsColumn || gap >= best.Gap) continue;
                var from = new DxfPoint(lng.Start.X + ux * Math.Min(0.0, s0), lng.Start.Y + uy * Math.Min(0.0, s0));
                double far = Math.Max(lng.Length, s1);
                var to = new DxfPoint(lng.Start.X + ux * far, lng.Start.Y + uy * far);
                best = (i, j, new WallAxis(from, to, lng.Thickness, lng.Layer), gap);
            }
            if (best.I < 0) return list;
            var facing = FacingEnds(list[best.I], list[best.J]);
            list.RemoveAt(best.J); list.RemoveAt(best.I);
            CarryStemsToTheRun(list, best.Merged, facing, inch);
            list.Add(best.Merged); joined++;
        }
    }

    /// <summary>The two piece ends that face across the gap - the ends a join makes disappear inside the run.</summary>
    private static (DxfPoint A, DxfPoint B) FacingEnds(WallAxis a, WallAxis b)
    {
        (DxfPoint P, DxfPoint Q, double D) best = (a.Start, b.Start, double.MaxValue);
        foreach (var p in new[] { a.Start, a.End })
        foreach (var q in new[] { b.Start, b.End })
            if (p.DistanceTo(q) < best.D) best = (p, q, p.DistanceTo(q));
        return (best.P, best.Q);
    }

    /// <summary>
    /// A WALL THAT MET A PIECE'S END STILL MEETS THE RUN (2026-10-28, board 76 consumer 1). Before the join a cross wall that
    /// stopped at the face of a piece's end made a CORNER with it, and the network carried both ends to the crossing. Once the
    /// pieces are one run that end is gone and the crossing is mid-span - a T, where the network moves no end - so the cross
    /// wall stopped half a thickness short (31053 L2 KW86, 280 mm). Here the joint the drawing had is kept: an end of another
    /// wall within the network's own corner reach of either vanishing end is carried to where its line crosses the run. Only
    /// those ends move; nothing that met the pieces anywhere else is touched.
    /// </summary>
    private static void CarryStemsToTheRun(List<WallAxis> walls, WallAxis run, (DxfPoint A, DxfPoint B) facing, double inch)
    {
        double rx = run.End.X - run.Start.X, ry = run.End.Y - run.Start.Y, rl = Math.Sqrt(rx * rx + ry * ry);
        if (rl < 1e-9) return;
        for (int k = 0; k < walls.Count; k++)
        {
            var w = walls[k];
            double reach = (run.Thickness + w.Thickness) / 2.0 * ReachFactor + 2.0 * inch;
            double wx = w.End.X - w.Start.X, wy = w.End.Y - w.Start.Y;
            double cross = rx * wy - ry * wx;
            if (Math.Abs(cross) < 1e-9 * rl * Math.Max(1e-9, Math.Sqrt(wx * wx + wy * wy))) continue;   // parallel: not a stem
            // where w's line meets the run's line, and how far along the run
            double s = ((w.Start.X - run.Start.X) * wy - (w.Start.Y - run.Start.Y) * wx) / cross;
            if (s <= 0 || s >= 1) continue;
            var at = new DxfPoint(run.Start.X + rx * s, run.Start.Y + ry * s);
            var start = w.Start; var end = w.End;
            if (Near(start)) start = at;
            else if (Near(end)) end = at;
            else continue;
            walls[k] = w with { Start = start, End = end };

            bool Near(DxfPoint p) => LoopGeometry.Within(p.DistanceTo(at), reach)
                                     && (LoopGeometry.Within(p.DistanceTo(facing.A), reach) || LoopGeometry.Within(p.DistanceTo(facing.B), reach));
        }
    }

    /// <summary>The same test on two finished centrelines - the classifier's stub rule asks it of what a short wall runs into.</summary>
    internal static bool NearCopies(WallAxis a, WallAxis b)
        => NearCopies(new Axis { A = a.Start, B = a.End, Thickness = a.Thickness, Layer = a.Layer }, new Axis { A = b.Start, B = b.End, Thickness = b.Thickness, Layer = b.Layer });

    /// <summary>One wall drawn twice: parallel within 2 deg, less than a wall's thickness apart, sharing half the shorter.</summary>
    private static bool NearCopies(Axis a, Axis b)
    {
        double la = a.Length, lb = b.Length;
        if (la < 1e-9 || lb < 1e-9) return false;
        double ux = (a.B.X - a.A.X) / la, uy = (a.B.Y - a.A.Y) / la;
        double vx = (b.B.X - b.A.X) / lb, vy = (b.B.Y - b.A.Y) / lb;
        if (Math.Abs(ux * vy - uy * vx) > Math.Sin(2.0 * Math.PI / 180.0)) return false;
        double apart = Math.Abs((b.A.X - a.A.X) * -uy + (b.A.Y - a.A.Y) * ux);
        if (apart > Math.Max(a.Thickness, b.Thickness)) return false;
        double t0 = (b.A.X - a.A.X) * ux + (b.A.Y - a.A.Y) * uy, t1 = (b.B.X - a.A.X) * ux + (b.B.Y - a.A.Y) * uy;
        double shared = Math.Min(la, Math.Max(t0, t1)) - Math.Max(0.0, Math.Min(t0, t1));
        return shared >= 0.5 * Math.Min(la, lb);
    }

    /// <summary>
    /// Pass 3 — a wall whose end lands part-way along another is split there.
    ///
    /// Without this the stem of a T shares no joint with the wall it runs into: ETABS meshes them
    /// independently and the connection carries nothing.
    /// </summary>
    private static List<Axis> SplitAtTJunctions(List<Axis> axes, double snap)
    {
        var joints = new List<DxfPoint>();
        foreach (var axis in axes) { joints.Add(axis.A); joints.Add(axis.B); }

        // WHERE TWO WALLS CROSS, BOTH ARE CUT.
        //
        // Until now this cut a wall only where another wall's END landed on it -- a T. Two walls
        // that cross with neither ending at the crossing were left whole, so they passed through
        // each other with no joint in common and carried no force between them. In ETABS they are
        // two shells that happen to overlap.
        //
        // The engineer, on a crossing at P2: "two walls that are joined on the drawings should be
        // shells in etabs, that intersect in one joint."
        //
        // Only a crossing INSIDE both walls is added; a crossing at or beyond an end is a corner,
        // which pass 1 already carries the ends out to.
        for (int i = 0; i < axes.Count; i++)
        for (int j = i + 1; j < axes.Count; j++)
        {
            if (!TryCross(axes[i], axes[j], out var cross)) continue;
            if (!Inside(axes[i], cross) || !Inside(axes[j], cross)) continue;
            joints.Add(cross);
        }

        bool Inside(Axis axis, DxfPoint at)
        {
            double dx = axis.B.X - axis.A.X, dy = axis.B.Y - axis.A.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-9) return false;

            double t = ((at.X - axis.A.X) * dx + (at.Y - axis.A.Y) * dy) / lengthSquared;
            if (t <= 0.001 || t >= 0.999) return false;

            var onAxis = new DxfPoint(axis.A.X + dx * t, axis.A.Y + dy * t);
            return LoopGeometry.Within(onAxis.DistanceTo(at), snap);
        }

        var result = new List<Axis>();

        foreach (var axis in axes)
        {
            double dx = axis.B.X - axis.A.X, dy = axis.B.Y - axis.A.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-9) { result.Add(axis); continue; }

            var cuts = new List<double>();
            foreach (var joint in joints)
            {
                if (LoopGeometry.Within(joint.DistanceTo(axis.A), snap) || LoopGeometry.Within(joint.DistanceTo(axis.B), snap)) continue;

                double t = ((joint.X - axis.A.X) * dx + (joint.Y - axis.A.Y) * dy) / lengthSquared;
                if (t <= 0.001 || t >= 0.999) continue;

                var onAxis = new DxfPoint(axis.A.X + dx * t, axis.A.Y + dy * t);
                if (LoopGeometry.Beyond(onAxis.DistanceTo(joint), snap)) continue;

                if (!cuts.Any(c => Math.Abs(c - t) < 1e-6)) cuts.Add(t);
            }

            if (cuts.Count == 0) { result.Add(axis); continue; }

            cuts.Sort();
            var from = axis.A;
            foreach (double t in cuts)
            {
                var at = new DxfPoint(axis.A.X + dx * t, axis.A.Y + dy * t);
                if (LoopGeometry.Beyond(from.DistanceTo(at), snap))
                    result.Add(new Axis { A = from, B = at, Thickness = axis.Thickness, Layer = axis.Layer });
                from = at;
            }
            if (LoopGeometry.Beyond(from.DistanceTo(axis.B), snap))
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
    ///
    /// The 3 in off line, 4 in of thickness and 6 in of shared middle are inches, scaled by <paramref name="inch"/>
    /// (2026-10-27: in a millimetre model they were 3, 4 and 6 mm).
    /// </summary>
    /// <param name="columns">This sheet's columns: a gap that holds one is not a doorway (<see cref="HoldsColumn"/>).</param>
    public static IReadOnlyList<WallOpening> FindOpenings(
        IReadOnlyList<WallAxis> walls, double minSpan, double maxSpan, double inch = 1.0, IReadOnlyList<ColumnFootprint>? columns = null)
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
            if (LoopGeometry.Beyond(offset, 3.0 * inch)) continue;

            // Similar thickness, or it is a different wall that happens to line up.
            if (LoopGeometry.Beyond(Math.Abs(a.Thickness - b.Thickness), 4.0 * inch)) continue;

            (DxfPoint P, DxfPoint Q, double D) best = (default, default, double.MaxValue);
            foreach (var p in new[] { a.Start, a.End })
            foreach (var q in new[] { b.Start, b.End })
            {
                double d = p.DistanceTo(q);
                if (d < best.D - 1e-6) best = (p, q, d);
            }

            if (LoopGeometry.Beyond(minSpan, best.D) || LoopGeometry.Beyond(best.D, maxSpan)) continue;   // a 24" doorway meets a 24" floor in every frame

            // A HEADER LIES ON ITS JAMBS' AXIS (2026-10-27, WallNetworkInvariant). Jambs up to 3 in out of line pass the test
            // above, and the header between their ends then runs crooked: 139 on the metric gate sets once the 3 in was 3 in
            // and not 3 mm. ASSUMED: no header where it would turn more than ToleranceDegrees off either jamb - what a metric
            // drawing got before (its 3 mm found none of these) - rather than a crooked one; the doorway is still a gap.
            double hx = best.Q.X - best.P.X, hy = best.Q.Y - best.P.Y, hl = Math.Sqrt(hx * hx + hy * hy);
            if (hl > 1e-9 && Math.Max(Off(hx, hy, hl, ax, ay, la), Off(hx, hy, hl, bx, by, lb)) > WallNetworkInvariant.ToleranceDegrees) continue;
            if (columns is { Count: > 0 } && HoldsColumn(best.P, best.Q, columns, inch)) continue;
            if (!used.Add((i, j))) continue;

            found.Add(new WallOpening(best.P, best.Q, Math.Min(a.Thickness, b.Thickness), a.Layer) { ThickerWall = Math.Max(a.Thickness, b.Thickness) });
        }

        // One doorway, one opening.
        //
        // A gap is bounded by two wall ends, but more than one PAIR of walls can describe it —
        // where a wall meets a return, the run continues through the junction and the same
        // doorway is found again between the far wall and the return. Two headers then land on
        // top of each other over one door, at whatever depths their two storeys imply.
        //
        // The tightest reading wins: the true doorway is the one between the NEAREST pair of
        // ends, and anything sharing its middle is that same gap seen from further away.
        var kept = new List<WallOpening>();
        foreach (var opening in found.OrderBy(o => Math.Round(o.Start.DistanceTo(o.End), 6)))
        {
            var mid = new DxfPoint((opening.Start.X + opening.End.X) / 2, (opening.Start.Y + opening.End.Y) / 2);
            if (kept.Any(k => LoopGeometry.Within(DistanceToSegment(mid, k.Start, k.End), 6.0 * inch))) continue;
            kept.Add(opening);
        }

        return kept;
    }

    /// <summary>
    /// A GAP THAT HOLDS A COLUMN IS NOT A DOORWAY (2026-10-28, board 52's narrow form; VD's decision). A drawing stops a wall at
    /// each face of a column standing in it, and the finder read the two pieces as jambs and spanned a header across the column:
    /// 84 of 3,177 headers on the 57 gate sets (tools/CorpusGateBisect/header_over_column.py). The walls stay as drawn - the full
    /// join that runs one wall through the column is board 76, held because it disturbed T joints, chaining and the perimeter-wall
    /// floor. A column holds the gap when its centre lies between the two ends and within 300 mm of the line between them, and the
    /// gap is no wider than the column's largest side, nor its extent along the gap, plus 6 in (ASSUMED slack): a wider gap is a
    /// doorway beside a column. 30972 P3/P2 (744 mm, 432x305 column, the engineer runs the wall through) stay a stated exception.
    /// </summary>
    internal static bool HoldsColumn(DxfPoint p, DxfPoint q, IReadOnlyList<ColumnFootprint> columns, double inch)
    {
        double gap = p.DistanceTo(q);
        if (gap < 1e-9) return false;
        double ux = (q.X - p.X) / gap, uy = (q.Y - p.Y) / gap;
        double reach = 300.0 / 25.4 * inch, slack = 6.0 * inch;
        double along = Math.Atan2(uy, ux) * 180.0 / Math.PI;
        return columns.Any(c =>
        {
            double t = (c.Center.X - p.X) * ux + (c.Center.Y - p.Y) * uy;
            if (t <= 0 || t >= gap || Math.Abs((c.Center.X - p.X) * -uy + (c.Center.Y - p.Y) * ux) > reach) return false;
            double r = (c.AxisAngleDegrees - along) * Math.PI / 180.0;
            double extent = Math.Abs(c.Depth * Math.Cos(r)) + Math.Abs(c.Width * Math.Sin(r));   // Depth runs along the column's axis
            return gap <= Math.Max(c.Width, c.Depth) + slack && gap <= extent + slack;
        });
    }

    /// <summary>The angle in degrees between two directions, either way round (0-90).</summary>
    private static double Off(double x1, double y1, double l1, double x2, double y2, double l2)
        => Math.Asin(Math.Min(1.0, Math.Abs(x1 * y2 - y1 * x2) / (l1 * l2))) * 180.0 / Math.PI;

    private static double DistanceToSegment(DxfPoint p, DxfPoint a, DxfPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9) return p.DistanceTo(a);

        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0.0, 1.0);
        return p.DistanceTo(new DxfPoint(a.X + t * dx, a.Y + t * dy));
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
