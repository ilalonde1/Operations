namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Stitches loose DXF segments back into closed rings.
///
/// Revit's CAD export writes plan outlines as individual LINE entities, not as
/// polylines, so a wall or slab boundary arrives as a pile of unordered segments.
/// Column and wall outlines close exactly; slab edges are usually interrupted where
/// they meet other linework, so a bounded gap-bridging pass runs afterwards and
/// anything still open is reported rather than silently closed.
/// </summary>
public sealed class PlanLoopBuilder
{
    private readonly double _joinTolerance;
    private readonly double _bridgeTolerance;
    private readonly double _extendLimit;

    /// <param name="joinTolerance">Endpoints closer than this are the same node (drawing units).</param>
    /// <param name="bridgeTolerance">How far apart two chain ends may be and still be joined.</param>
    /// <param name="extendLimit">How far an interrupted edge may be carried forward to its corner.</param>
    public PlanLoopBuilder(double joinTolerance = 0.05, double bridgeTolerance = 6.0, double extendLimit = 48.0)
    {
        _joinTolerance = joinTolerance;
        _bridgeTolerance = bridgeTolerance;
        _extendLimit = extendLimit;
    }

    public sealed record Result(IReadOnlyList<PlanLoop> Loops, IReadOnlyList<IReadOnlyList<DxfPoint>> OpenChains);

    public Result Build(IEnumerable<DxfSegment> segments)
    {
        // KNOWN, MEASURED, NOT FIXED (audit F11, step 61, 2026-09-14): the walk is seeded in arrival order and
        // the nodes are numbered by it, so the same drawings with their entities reversed build other walls on
        // every one of the six sets (TheSameDrawingsInAnotherOrderBuildTheSameStructureTests, skipped red:
        // 31168 473 walls lost / 241 gained, 31065 134 / 161). TRIED AND REVERTED THE SAME NIGHT: one canonical
        // geometric order (each segment from its lesser end, sorted by that end then the other). It moved every
        // set's walls (31130: 117 lost / 99 gained against the bank), was still not the same under reversal on
        // 31065 (6 columns), and moved a plate under the page-shift differential that had been green - an order
        // keyed on floating coordinates is the frame class of s63 by another door. The next cut needs a rule
        // for which ring a shared edge belongs to, not a sort; see s70.
        var segs = segments.ToList();
        if (segs.Count == 0)
            return new Result(Array.Empty<PlanLoop>(), Array.Empty<IReadOnlyList<DxfPoint>>());

        string layer = segs[0].Layer;

        // A NODE IS THE NEAREST NODE WITHIN THE JOIN TOLERANCE, BY DISTANCE (intake step 56, 2026-09-13). It was
        // the cell of the tolerance a point fell in, so two ends a tenth of a millimetre apart either side of a
        // cell's edge were two nodes and the outline did not close, and which pairs of ends straddled an edge
        // was a matter of where the drawing's origin was: the same drawings shifted 5 m x 3 m on the page read
        // 9 of 31202's sheets differently - a 6" wall along the tops of two 48" piers came out left of the first
        // pier in one frame and right of the second in the other. The cells are an index for the search; the
        // distance decides, and a cell may hold more than one node (its diagonal is 1.4 tolerances).
        var nodes = new Dictionary<(long, long), List<int>>();
        var nodePoints = new List<DxfPoint>();
        int NodeOf(DxfPoint p)
        {
            var key = ((long)Math.Round(p.X / _joinTolerance), (long)Math.Round(p.Y / _joinTolerance));
            int nearest = -1;
            double nearestDistance = double.MaxValue;                          // a node AT the tolerance is within it (audit F13: Within, to the micron)
            for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
            {
                if (!nodes.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var cell)) continue;
                foreach (int id in cell)
                {
                    // and a tie between two nodes goes to the earlier one, not to whichever cell the
                    // search happened to visit first (the cells' order is the frame's)
                    double d = nodePoints[id].DistanceTo(p);
                    if (!LoopGeometry.Within(d, _joinTolerance)) continue;
                    if (d < nearestDistance - 1e-9 || (Math.Abs(d - nearestDistance) <= 1e-9 && nearest >= 0 && id < nearest)) { nearestDistance = d; nearest = id; }
                }
            }
            if (nearest >= 0) return nearest;

            int made = nodePoints.Count;
            (nodes.TryGetValue(key, out var mine) ? mine : nodes[key] = new List<int>()).Add(made);
            nodePoints.Add(p);
            return made;
        }

        var edgeA = new int[segs.Count];
        var edgeB = new int[segs.Count];
        var adjacency = new Dictionary<int, List<int>>();
        void Link(int node, int edge)
        {
            if (!adjacency.TryGetValue(node, out var list)) adjacency[node] = list = new List<int>();
            list.Add(edge);
        }

        for (int e = 0; e < segs.Count; e++)
        {
            edgeA[e] = NodeOf(segs[e].Start);
            edgeB[e] = NodeOf(segs[e].End);
            if (edgeA[e] == edgeB[e]) continue;
            Link(edgeA[e], e);
            Link(edgeB[e], e);
        }

        var used = new bool[segs.Count];
        var loops = new List<PlanLoop>();
        var chains = new List<List<DxfPoint>>();

        for (int seed = 0; seed < segs.Count; seed++)
        {
            if (used[seed] || edgeA[seed] == edgeB[seed]) continue;

            int startNode = edgeA[seed];
            int currentNode = edgeB[seed];
            used[seed] = true;

            var path = new List<int> { startNode, currentNode };
            int previousEdge = seed;

            while (true)
            {
                if (currentNode == startNode) break;

                int nextEdge = PickContinuation(currentNode, previousEdge, adjacency, used, edgeA, edgeB, nodePoints, path);
                if (nextEdge < 0) break;

                used[nextEdge] = true;
                currentNode = edgeA[nextEdge] == currentNode ? edgeB[nextEdge] : edgeA[nextEdge];
                path.Add(currentNode);
                previousEdge = nextEdge;
            }

            var points = path.Select(n => nodePoints[n]).ToList();
            bool closed = points.Count > 3 && path[^1] == startNode;

            if (closed)
            {
                points.RemoveAt(points.Count - 1);
                var simplified = LoopGeometry.Simplify(points, _joinTolerance);
                if (simplified.Count >= 3) loops.Add(new PlanLoop(layer, simplified, closedExactly: true));
            }
            else if (points.Count >= 2)
            {
                chains.Add(points);
            }
        }

        var (bridgedLoops, stillOpen) = BridgeChains(layer, chains);
        loops.AddRange(bridgedLoops);

        return new Result(loops, stillOpen);
    }

    /// <summary>
    /// At a junction, continue along the segment that deviates least from the current
    /// heading. Following the straightest path keeps a slab boundary on its own outline
    /// instead of turning down whatever linework happens to touch it.
    /// </summary>
    private static int PickContinuation(
        int node, int previousEdge, Dictionary<int, List<int>> adjacency, bool[] used,
        int[] edgeA, int[] edgeB, List<DxfPoint> nodePoints, List<int> path)
    {
        if (!adjacency.TryGetValue(node, out var candidates)) return -1;

        int fromNode = edgeA[previousEdge] == node ? edgeB[previousEdge] : edgeA[previousEdge];
        var incoming = Direction(nodePoints[fromNode], nodePoints[node]);

        int best = -1;
        double bestScore = double.MaxValue, bestLength = double.MaxValue;
        bool bestCloses = false;
        int startNode = path.Count > 0 ? path[0] : -1;

        foreach (int e in candidates)
        {
            if (used[e]) continue;
            int other = edgeA[e] == node ? edgeB[e] : edgeA[e];
            var outgoing = Direction(nodePoints[node], nodePoints[other]);

            // 0 for dead straight, 2 for a full reversal.
            double score = 1.0 - (incoming.X * outgoing.X + incoming.Y * outgoing.Y);
            // TWO EDGES LEAVING ONE WAY ARE A TIE, AND A TIE IS DECIDED BY THE OUTLINE, NOT BY THE ORDER THE
            // EDGES CAME IN (intake step 56, 2026-09-13). At the outer corner of a core's 30x41 return, the
            // return's own bottom edge and the core's 9 m bottom face leave the corner along one line; taking
            // the long face strung both returns into one open chain that swallowed the south wall's face,
            // and the wall was never paired (31168's tower A core: 18 walls in one order of the edges, 8 in
            // the other). The edge that closes the outline wins; then the shorter, which stays on the shape
            // being traced; then the first.
            bool closes = other == startNode;
            double length = nodePoints[node].DistanceTo(nodePoints[other]);
            bool better = score < bestScore - 1e-9
                || (Math.Abs(score - bestScore) <= 1e-9 && (closes && !bestCloses || (closes == bestCloses && length < bestLength - 1e-6)));
            if (better)
            {
                bestScore = score;
                bestLength = length;
                bestCloses = closes;
                best = e;
            }
        }

        return best;
    }

    /// <summary>
    /// Joins two outlines that were cut apart at a corner.
    ///
    /// Where another element crosses a slab edge, the export stops one run short of the corner
    /// and starts the next run past it, leaving a gap far too wide to bridge by distance — but
    /// both runs still point at the corner. Carrying each forward along its own direction finds
    /// it, which reconstructs the outline as drawn instead of cutting the corner off.
    /// </summary>
    private bool TryJoinByExtending(List<DxfPoint> a, List<DxfPoint> b, out List<DxfPoint>? joined)
    {
        joined = null;
        if (a.Count < 2 || b.Count < 2) return false;

        // Try every pairing of the two chains' ends, orienting each so that a's tail meets b's head.
        for (int orientation = 0; orientation < 4; orientation++)
        {
            var first = orientation is 0 or 1 ? a : Reversed(a);
            var second = orientation is 0 or 2 ? b : Reversed(b);

            var corner = RayIntersection(first[^2], first[^1], second[1], second[0]);
            if (corner is null) continue;

            var result = new List<DxfPoint>(first) { corner.Value };
            result.AddRange(second);
            joined = result;
            return true;
        }

        return false;
    }

    private static List<DxfPoint> Reversed(List<DxfPoint> points)
    {
        var copy = new List<DxfPoint>(points);
        copy.Reverse();
        return copy;
    }

    /// <summary>
    /// Where the ray leaving <paramref name="tailFrom"/>→<paramref name="tailTo"/> meets the ray
    /// arriving at <paramref name="headTo"/> from <paramref name="headFrom"/>. Null when they are
    /// parallel, when the meeting point lies behind either run, or when either must reach further
    /// than the extend limit — a cut outline is a short reach, not a projection across the plate.
    /// </summary>
    private DxfPoint? RayIntersection(DxfPoint tailFrom, DxfPoint tailTo, DxfPoint headFrom, DxfPoint headTo)
    {
        double r1x = tailTo.X - tailFrom.X, r1y = tailTo.Y - tailFrom.Y;
        double r2x = headTo.X - headFrom.X, r2y = headTo.Y - headFrom.Y;

        double denominator = r1x * r2y - r1y * r2x;
        if (Math.Abs(denominator) < 1e-9) return null;

        double dx = headTo.X - tailTo.X, dy = headTo.Y - tailTo.Y;
        double s = (dx * r2y - dy * r2x) / denominator;
        if (s < 0) return null;   // the corner would be behind the tail run

        var corner = new DxfPoint(tailTo.X + r1x * s, tailTo.Y + r1y * s);

        // The head run must also reach forward to the corner, not away from it.
        double towards = (corner.X - headTo.X) * -r2x + (corner.Y - headTo.Y) * -r2y;
        if (towards < 0) return null;

        double reach = Math.Max(tailTo.DistanceTo(corner), headTo.DistanceTo(corner));
        return LoopGeometry.Within(reach, _extendLimit) ? corner : null;
    }

    /// <summary>
    /// Where the chain's two end runs would meet if each carried on. Returns null when they
    /// are parallel, when the corner sits behind either run, or when it lies improbably far
    /// out — an interrupted outline is a short reach, not a projection across the plate.
    /// </summary>
    private DxfPoint? ExtendToIntersection(IReadOnlyList<DxfPoint> chain)
    {
        var tailFrom = chain[^2];
        var tailTo = chain[^1];
        var headFrom = chain[1];
        var headTo = chain[0];

        double r1x = tailTo.X - tailFrom.X, r1y = tailTo.Y - tailFrom.Y;
        double r2x = headTo.X - headFrom.X, r2y = headTo.Y - headFrom.Y;

        double denominator = r1x * r2y - r1y * r2x;
        if (Math.Abs(denominator) < 1e-9) return null;

        double dx = headTo.X - tailTo.X, dy = headTo.Y - tailTo.Y;
        double s = (dx * r2y - dy * r2x) / denominator;
        double t = (dx * r1y - dy * r1x) / denominator;

        // Both runs must reach forward to the corner, not backwards along themselves.
        if (s < 0 || t > 0) return null;

        var corner = new DxfPoint(tailTo.X + r1x * s, tailTo.Y + r1y * s);
        double reach = Math.Max(tailTo.DistanceTo(corner), headTo.DistanceTo(corner));
        return LoopGeometry.Within(reach, _extendLimit) ? corner : null;
    }

    private static DxfPoint Direction(DxfPoint from, DxfPoint to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-9 ? new DxfPoint(0, 0) : new DxfPoint(dx / len, dy / len);
    }

    /// <summary>Joins chain ends that are within the bridge tolerance, then closes whatever became a ring.</summary>
    private (List<PlanLoop> Loops, List<IReadOnlyList<DxfPoint>> Open) BridgeChains(string layer, List<List<DxfPoint>> chains)
    {
        var loops = new List<PlanLoop>();
        var work = chains.Where(c => c.Count >= 2).ToList();

        // NOTHING TO BRIDGE, NOTHING TO SEARCH. Two chain ends within the join tolerance are
        // already one node, so a bridge no wider than the join and an extension no longer than it
        // can never fire — and the search below is every pair of chains, restarted after every
        // merge. Asked for exact rings only (the PDF side's slab edge, intake step 24), a hatched
        // plan with ten thousand dashes spent minutes here finding nothing.
        if (_bridgeTolerance <= _joinTolerance && _extendLimit <= _joinTolerance)
            return (loops, work.Select(c => (IReadOnlyList<DxfPoint>)c).ToList());

        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < work.Count && !merged; i++)
            {
                for (int j = i + 1; j < work.Count && !merged; j++)
                {
                    var a = work[i];
                    var b = work[j];

                    // to the micron (LoopGeometry.Within): a 12" gap meets a 12" bridge in every frame
                    if (LoopGeometry.Within(a[^1].DistanceTo(b[0]), _bridgeTolerance)) { a.AddRange(b); }
                    else if (LoopGeometry.Within(a[^1].DistanceTo(b[^1]), _bridgeTolerance)) { b.Reverse(); a.AddRange(b); }
                    else if (LoopGeometry.Within(a[0].DistanceTo(b[^1]), _bridgeTolerance)) { b.AddRange(a); work[i] = b; }
                    else if (LoopGeometry.Within(a[0].DistanceTo(b[0]), _bridgeTolerance)) { a.Reverse(); a.AddRange(b); }
                    else if (TryJoinByExtending(a, b, out var joined)) { work[i] = joined!; }
                    else continue;

                    work.RemoveAt(j);
                    merged = true;
                }
            }
        }

        var open = new List<IReadOnlyList<DxfPoint>>();
        foreach (var chain in work)
        {
            var candidate = chain;

            // A chain whose ends run past each other closes where those runs cross. Drafting
            // interrupts an outline at a junction, so the two ends still point at their true
            // corner even though their endpoints are far apart — extending finds it, whereas
            // joining by distance would cut the corner off.
            if (candidate.Count >= 4 && LoopGeometry.Beyond(candidate[0].DistanceTo(candidate[^1]), _joinTolerance))
            {
                var corner = ExtendToIntersection(candidate);
                if (corner is not null) candidate = new List<DxfPoint>(candidate) { corner.Value };
            }

            if (candidate.Count >= 4 && LoopGeometry.Within(candidate[0].DistanceTo(candidate[^1]), _bridgeTolerance))
            {
                var pts = LoopGeometry.Simplify(candidate, _joinTolerance);
                if (pts.Count >= 3)
                {
                    loops.Add(new PlanLoop(layer, pts, closedExactly: false));
                    continue;
                }
            }
            open.Add(chain);
        }

        return (loops, open);
    }
}
