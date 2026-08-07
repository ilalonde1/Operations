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

    /// <param name="joinTolerance">Endpoints closer than this are the same node (drawing units).</param>
    /// <param name="bridgeTolerance">How far apart two chain ends may be and still be joined.</param>
    public PlanLoopBuilder(double joinTolerance = 0.05, double bridgeTolerance = 6.0)
    {
        _joinTolerance = joinTolerance;
        _bridgeTolerance = bridgeTolerance;
    }

    public sealed record Result(IReadOnlyList<PlanLoop> Loops, IReadOnlyList<IReadOnlyList<DxfPoint>> OpenChains);

    public Result Build(IEnumerable<DxfSegment> segments)
    {
        var segs = segments.ToList();
        if (segs.Count == 0)
            return new Result(Array.Empty<PlanLoop>(), Array.Empty<IReadOnlyList<DxfPoint>>());

        string layer = segs[0].Layer;

        var nodes = new Dictionary<(long, long), int>();
        var nodePoints = new List<DxfPoint>();
        int NodeOf(DxfPoint p)
        {
            var key = ((long)Math.Round(p.X / _joinTolerance), (long)Math.Round(p.Y / _joinTolerance));
            if (!nodes.TryGetValue(key, out int id))
            {
                id = nodePoints.Count;
                nodes[key] = id;
                nodePoints.Add(p);
            }
            return id;
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
        double bestScore = double.MaxValue;

        foreach (int e in candidates)
        {
            if (used[e]) continue;
            int other = edgeA[e] == node ? edgeB[e] : edgeA[e];
            var outgoing = Direction(nodePoints[node], nodePoints[other]);

            // 0 for dead straight, 2 for a full reversal.
            double score = 1.0 - (incoming.X * outgoing.X + incoming.Y * outgoing.Y);
            if (score < bestScore)
            {
                bestScore = score;
                best = e;
            }
        }

        return best;
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

                    if (a[^1].DistanceTo(b[0]) <= _bridgeTolerance) { a.AddRange(b); }
                    else if (a[^1].DistanceTo(b[^1]) <= _bridgeTolerance) { b.Reverse(); a.AddRange(b); }
                    else if (a[0].DistanceTo(b[^1]) <= _bridgeTolerance) { b.AddRange(a); work[i] = b; }
                    else if (a[0].DistanceTo(b[0]) <= _bridgeTolerance) { a.Reverse(); a.AddRange(b); }
                    else continue;

                    work.RemoveAt(j);
                    merged = true;
                }
            }
        }

        var open = new List<IReadOnlyList<DxfPoint>>();
        foreach (var chain in work)
        {
            if (chain.Count >= 4 && chain[0].DistanceTo(chain[^1]) <= _bridgeTolerance)
            {
                var pts = LoopGeometry.Simplify(chain, _joinTolerance);
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
