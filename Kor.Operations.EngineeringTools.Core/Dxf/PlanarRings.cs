#nullable enable
namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Every directed edge bounds the face on its left; a shared undirected edge bounds both faces.
/// WHAT THIS COVERS: straight-segment arrangements, transitive endpoint clustering, intersections,
/// overlapping edges, bounded faces with hole boundaries, and conservative gap proposals made
/// simultaneously before face enumeration. Geometry, never input indices, decides adjacency.
/// WHAT IT DOES NOT: infer structural roles or openings, promise output-list order, or preserve the
/// old straightest walk. Transitive chains can collapse a feature wider than the join tolerance.
/// Finite-precision degeneracies that leave a nonplanar graph or coincident rays are rejected,
/// not resolved by arrival order. The class is intentionally not wired into existing readers.
/// </summary>
public sealed class PlanarRings
{
    private readonly double _joinTolerance;
    private readonly double _bridgeTolerance;
    private readonly double _extendLimit;

    public PlanarRings(double joinTolerance = 0.05, double bridgeTolerance = 6.0, double extendLimit = 48.0)
    {
        if (!double.IsFinite(joinTolerance) || joinTolerance <= 0) throw new ArgumentOutOfRangeException(nameof(joinTolerance));
        if (!double.IsFinite(bridgeTolerance) || bridgeTolerance < 0) throw new ArgumentOutOfRangeException(nameof(bridgeTolerance));
        if (!double.IsFinite(extendLimit) || extendLimit < 0) throw new ArgumentOutOfRangeException(nameof(extendLimit));
        _joinTolerance = joinTolerance;
        _bridgeTolerance = bridgeTolerance;
        _extendLimit = extendLimit;
    }

    /// <summary>Outer boundary counterclockwise; hole boundaries clockwise. Holes are not filled by this face.</summary>
    public sealed record Face(PlanLoop Outer, IReadOnlyList<PlanLoop> Holes);
    public sealed record Surfaces(IReadOnlyList<Face> WallBands, IReadOnlyList<Face> Slabs);

    public sealed record Result(IReadOnlyList<PlanLoop> Loops, IReadOnlyList<IReadOnlyList<DxfPoint>> OpenChains)
    {
        public IReadOnlyList<Face> Faces { get; init; } = [];
        internal Mesh? Topology { get; init; }

        /// <summary>
        /// Two independent interpretations of the same cells. Selecting slab cells on BOTH sides
        /// of a wall and the band itself recovers one plate; excluding the band makes a slot.
        /// The caller must identify void cells. Topology cannot distinguish a hole from a room.
        /// </summary>
        public Surfaces RecoverSurfaces(Func<Face, bool> isWallBand, Func<Face, bool> isSlabCell)
        {
            ArgumentNullException.ThrowIfNull(isWallBand);
            ArgumentNullException.ThrowIfNull(isSlabCell);
            var mesh = Topology ?? throw new InvalidOperationException("Surface recovery requires a Build result.");
            var selected = Faces.Select(isSlabCell).ToArray();
            bool Filled(int h) => mesh.Owner[h] >= 0 && selected[mesh.Owner[h]];
            var boundary = Enumerable.Range(0, mesh.Owner.Length).Select(h => Filled(h) && !Filled(h ^ 1)).ToArray();
            var cycles = Walk(mesh, boundary);
            return new Surfaces(Faces.Where(isWallBand).ToList(), Regions(mesh, cycles, separateComponents: false));
        }
    }

    /// <summary>
    /// A deliberately narrow band predicate: a rectangle, within the caller's thickness floor/cap
    /// and aspect floor, with no holes. Compound wall ribbons require the consumer's own predicate.
    /// All lengths are in the drawing's unit; this method contains no wall-thickness default.
    /// </summary>
    public static bool IsRectangularBand(Face face, double minimumThickness, double maximumThickness,
        double joinTolerance = 0.05, double minimumAspect = 2.0)
    {
        if (minimumThickness <= 0 || maximumThickness < minimumThickness || minimumAspect < 1
            || !double.IsFinite(minimumThickness) || !double.IsFinite(maximumThickness)
            || !double.IsFinite(minimumAspect) || !double.IsFinite(joinTolerance) || joinTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumThickness));
        var p = face.Outer.Points;
        if (face.Holes.Count != 0 || p.Count != 4) return false;
        var lengths = Enumerable.Range(0, 4).Select(i => p[i].DistanceTo(p[(i + 1) % 4])).ToArray();
        if (lengths.Any(l => l <= 0)) return false;
        for (int i = 0; i < 4; i++)
        {
            var a = Sub(p[(i + 1) % 4], p[i]);
            var b = Sub(p[(i + 2) % 4], p[(i + 1) % 4]);
            if (LoopGeometry.Beyond(Math.Abs(Dot(a, b)) / lengths[i], joinTolerance)) return false;
            if (LoopGeometry.Beyond(Math.Abs(lengths[i] - lengths[(i + 2) % 4]), joinTolerance)) return false;
        }
        double width = lengths.Min(), length = lengths.Max();
        return LoopGeometry.Within(minimumThickness, width) && LoopGeometry.Within(width, maximumThickness)
            && LoopGeometry.Within(minimumAspect * width, length);
    }

    public Result Build(IEnumerable<DxfSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var input = segments.ToList();
        foreach (var s in input)
            foreach (var p in new[] { s.Start, s.End })
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 1e9 || Math.Abs(p.Y) > 1e9)
                    throw new ArgumentException("PlanarRings requires finite coordinates within +/-1e9 drawing units.", nameof(segments));
        // This is a TEXT provenance label, not a geometric ordering or a selection of structural roles.
        string layer = input.Select(s => s.Layer).OrderBy(s => s, StringComparer.Ordinal).FirstOrDefault() ?? "";
        var mesh = Arrange(input.Select(s => new Span(s.Start, s.End, false)).ToList(), layer);
        var additions = Bridges(mesh);
        if (additions.Count > 0)
            mesh = Arrange(mesh.Edges.Select(e => new Span(mesh.Points[e.A], mesh.Points[e.B], e.Inserted)).Concat(additions).ToList(), layer);
        mesh.Cut = CutEdges(mesh);
        mesh.Stars = Stars(mesh);
        var cycles = Walk(mesh, Enumerable.Range(0, mesh.Edges.Count * 2).Select(h => !mesh.Cut[h / 2]).ToArray());
        var faces = Regions(mesh, cycles, separateComponents: true);
        return new Result(faces.Select(f => f.Outer).ToList(), Chains(mesh)) { Faces = faces, Topology = mesh };
    }

    internal sealed record Edge(int A, int B, bool Inserted);
    private sealed record Span(DxfPoint A, DxfPoint B, bool Inserted);
    private sealed record Cycle(List<int> Halves, PlanLoop Loop, double Area, int Component);
    private sealed record Proposal(int A, int B, double Cost, List<Span> Spans);
    internal sealed class Mesh(string layer, double tolerance, List<DxfPoint> points, List<Edge> edges)
    {
        internal readonly string Layer = layer;
        internal readonly double Tolerance = tolerance;
        internal readonly List<DxfPoint> Points = points;
        internal readonly List<Edge> Edges = edges;
        internal readonly List<int>[] Adjacency = Enumerable.Range(0, points.Count).Select(_ => new List<int>()).ToArray();
        internal bool[] Cut = [];
        internal List<int>[] Stars = [];
        internal int[] Owner = Enumerable.Repeat(-1, edges.Count * 2).ToArray();
        internal int From(int h) => (h & 1) == 0 ? Edges[h / 2].A : Edges[h / 2].B;
        internal int To(int h) => From(h ^ 1);
        internal int Other(int e, int v) => Edges[e].A == v ? Edges[e].B : Edges[e].A;
    }

    private Mesh Arrange(List<Span> source, string layer)
    {
        var seen = new Dictionary<(DxfPoint, DxfPoint), int>();
        var lines = new List<Span>();
        foreach (var s in source)
        {
            if (s.A == s.B) continue;
            if (seen.TryGetValue((s.A, s.B), out int old) || seen.TryGetValue((s.B, s.A), out old))
                lines[old] = lines[old] with { Inserted = lines[old].Inserted && s.Inserted };
            else { seen[(s.A, s.B)] = lines.Count; lines.Add(s); }
        }
        var cuts = lines.Select(s => new List<DxfPoint> { s.A, s.B }).ToArray();
        for (int i = 0; i < lines.Count; i++)
            for (int j = i + 1; j < lines.Count; j++)
            {
                var a = lines[i]; var b = lines[j];
                if (!BoxesMeet(a, b, _joinTolerance + 1e-6)) continue;
                AddEnd(a.A, b, cuts[j]); AddEnd(a.B, b, cuts[j]);
                AddEnd(b.A, a, cuts[i]); AddEnd(b.B, a, cuts[i]);
                if (Intersection(a, b, out var p, out double t, out double u) && t >= 0 && t <= 1 && u >= 0 && u <= 1)
                { cuts[i].Add(p); cuts[j].Add(p); }
            }
        // a computed crossing lands a billionth from the endpoint it coincides with ((0.04, 0) meets (0.04000000000000001, 0)
        // where a slanted side ends on the bottom line); one sample, not two, or the corner's centroid leans toward it.
        // A billionth cannot move a sample across a join tolerance of hundredths, so this is not the cell class.
        static DxfPoint Sample(DxfPoint p) => new(Math.Round(p.X, 9), Math.Round(p.Y, 9));
        var samples = cuts.SelectMany(c => c).Select(Sample).Distinct().ToList();
        var index = samples.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        var parent = Enumerable.Range(0, samples.Count).ToArray();
        int Root(int n) { while (parent[n] != n) { parent[n] = parent[parent[n]]; n = parent[n]; } return n; }
        var cells = new Dictionary<(long, long), List<int>>();
        double cellSize = Math.Max(1e-6, Math.Round(_joinTolerance, 6) + 1e-6);
        for (int i = 0; i < samples.Count; i++)
        {
            var p = samples[i];
            var key = ((long)Math.Floor(p.X / cellSize), (long)Math.Floor(p.Y / cellSize));
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (cells.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var near))
                        foreach (int j in near)
                            if (LoopGeometry.Within(p.DistanceTo(samples[j]), _joinTolerance)) parent[Root(i)] = Root(j);
            if (!cells.TryGetValue(key, out var bucket)) cells[key] = bucket = [];
            bucket.Add(i);
        }
        var groups = Enumerable.Range(0, samples.Count).GroupBy(Root).ToList();
        var nodes = groups.Select(g => Mean(g.Select(i => samples[i]).ToList())).ToList();
        var nodeOf = new int[samples.Count];
        for (int n = 0; n < groups.Count; n++) foreach (int i in groups[n]) nodeOf[i] = n;
        var edges = new List<Edge>();
        var edgeIndex = new Dictionary<(int, int), int>();
        for (int i = 0; i < lines.Count; i++)
        {
            var s = lines[i];
            // Parametric order ALONG THIS SEGMENT is incidence, not a global X/Y seed order.
            var along = cuts[i].Select(p => nodeOf[index[Sample(p)]]).Distinct().OrderBy(n => Parameter(nodes[n], s)).ToList();
            for (int k = 1; k < along.Count; k++)
            {
                int a = along[k - 1], b = along[k];
                if (Parameter(nodes[a], s) == Parameter(nodes[b], s))
                    throw new InvalidOperationException("Distinct nodes occupy the same position along a segment after snapping.");
                var key = (Math.Min(a, b), Math.Max(a, b)); // Undirected identity only; IDs never break geometry ties.
                if (edgeIndex.TryGetValue(key, out int e)) edges[e] = edges[e] with { Inserted = edges[e].Inserted && s.Inserted };
                else { edgeIndex[key] = edges.Count; edges.Add(new Edge(a, b, s.Inserted)); }
            }
        }
        var mesh = new Mesh(layer, _joinTolerance, nodes, edges);
        for (int e = 0; e < edges.Count; e++) { mesh.Adjacency[edges[e].A].Add(e); mesh.Adjacency[edges[e].B].Add(e); }
        // Centroid snapping can change an embedding. Do not silently enumerate a nonplanar result.
        for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
            {
                var a = edges[i]; var b = edges[j];
                if (a.A == b.A || a.A == b.B || a.B == b.A || a.B == b.B) continue;
                if (Conflicts(new Span(nodes[a.A], nodes[a.B], false), new Span(nodes[b.A], nodes[b.B], false), false))
                    throw new InvalidOperationException("Centroid snapping left an unresolved crossing or contact; the embedding must be refined.");
            }
        return mesh;

        void AddEnd(DxfPoint p, Span s, List<DxfPoint> into)
        {
            double t = Parameter(p, s);
            if (t < 0 || t > 1) return;
            if (LoopGeometry.Within(p.DistanceTo(At(s, t)), _joinTolerance)) into.Add(p);
        }
    }

    private List<Span> Bridges(Mesh mesh)
    {
        if (_bridgeTolerance <= _joinTolerance && _extendLimit <= _joinTolerance) return [];
        var ends = Enumerable.Range(0, mesh.Points.Count).Where(i => mesh.Adjacency[i].Count == 1).ToList();
        var candidates = new List<Proposal>();
        for (int i = 0; i < ends.Count; i++)
            for (int j = i + 1; j < ends.Count; j++)
            {
                int a = ends[i], b = ends[j];
                if (mesh.Adjacency[a][0] == mesh.Adjacency[b][0]) continue;
                var p = mesh.Points[a]; var q = mesh.Points[b];
                var pa = mesh.Points[mesh.Other(mesh.Adjacency[a][0], a)];
                var qb = mesh.Points[mesh.Other(mesh.Adjacency[b][0], b)];
                var ra = new Span(p, Add(p, Sub(p, pa)), true);
                var rb = new Span(q, Add(q, Sub(q, qb)), true);
                var pieces = new List<Span>();
                if (Intersection(ra, rb, out var corner, out double t, out double u) && t >= 0 && u >= 0
                    && LoopGeometry.Within(Math.Max(p.DistanceTo(corner), q.DistanceTo(corner)), _extendLimit))
                { if (p != corner) pieces.Add(new Span(p, corner, true)); if (q != corner) pieces.Add(new Span(corner, q, true)); }
                else if (LoopGeometry.Within(p.DistanceTo(q), _bridgeTolerance)) pieces.Add(new Span(p, q, true));
                if (pieces.Count == 0 || pieces.Any(s => mesh.Edges.Any(e => Conflicts(s, new Span(mesh.Points[e.A], mesh.Points[e.B], false), true)))) continue;
                candidates.Add(new Proposal(a, b, Math.Round(pieces.Sum(s => s.A.DistanceTo(s.B)), 6), pieces));
            }
        var unique = new Dictionary<int, Proposal>();
        foreach (int end in ends)
        {
            var choices = candidates.Where(c => c.A == end || c.B == end).ToList();
            if (choices.Count == 0) continue;
            double best = choices.Min(c => c.Cost);
            var tied = choices.Where(c => c.Cost == best).ToList();
            if (tied.Count == 1) unique[end] = tied[0];
        }
        var agreed = candidates.Where(c => unique.GetValueOrDefault(c.A) == c && unique.GetValueOrDefault(c.B) == c).ToList();
        // Crossing proposals are BOTH refused; processing one first would reintroduce ownership by arrival.
        return agreed.Where(c => !agreed.Any(o => !ReferenceEquals(c, o)
                && c.Spans.Any(a => o.Spans.Any(b => Conflicts(a, b, false)))))
            .SelectMany(c => c.Spans).ToList();
    }

    private static bool[] CutEdges(Mesh mesh)
    {
        // Iterative Tarjan: degree-one pruning alone misses a bridge joining two closed cycles.
        int n = mesh.Points.Count, clock = 0;
        var discovered = new int[n]; var low = new int[n]; var next = new int[n];
        var parentEdge = Enumerable.Repeat(-1, n).ToArray();
        var cut = new bool[mesh.Edges.Count];
        for (int root = 0; root < n; root++)
        {
            if (discovered[root] != 0) continue;
            var stack = new Stack<int>(); stack.Push(root); discovered[root] = low[root] = ++clock;
            while (stack.Count > 0)
            {
                int v = stack.Peek();
                if (next[v] < mesh.Adjacency[v].Count)
                {
                    int e = mesh.Adjacency[v][next[v]++];
                    if (e == parentEdge[v]) continue;
                    int w = mesh.Other(e, v);
                    if (discovered[w] == 0) { parentEdge[w] = e; discovered[w] = low[w] = ++clock; stack.Push(w); }
                    else low[v] = Math.Min(low[v], discovered[w]);
                }
                else
                {
                    stack.Pop(); int e = parentEdge[v];
                    if (e < 0) continue;
                    int p = mesh.Other(e, v);
                    cut[e] = low[v] > discovered[p]; low[p] = Math.Min(low[p], low[v]);
                }
            }
        }
        return cut;
    }

    private static List<int>[] Stars(Mesh mesh)
    {
        var stars = Enumerable.Range(0, mesh.Points.Count).Select(_ => new List<int>()).ToArray();
        for (int h = 0; h < mesh.Edges.Count * 2; h++) if (!mesh.Cut[h / 2]) stars[mesh.From(h)].Add(h);
        double Angle(int h)
        {
            var d = Sub(mesh.Points[mesh.To(h)], mesh.Points[mesh.From(h)]);
            double angle = Math.Atan2(d.Y, d.X);
            return angle < 0 ? angle + 2 * Math.PI : angle;
        }
        foreach (var star in stars)
        {
            star.Sort((a, b) => Angle(a).CompareTo(Angle(b)));
            for (int i = 1; i < star.Count; i++)
                if (Angle(star[i - 1]) == Angle(star[i]))
                    throw new InvalidOperationException("Two distinct outgoing edges have the same angle; an overlap remains unresolved.");
        }
        return stars;
    }

    private static List<Cycle> Walk(Mesh mesh, bool[] active)
    {
        var next = Enumerable.Repeat(-1, active.Length).ToArray();
        for (int h = 0; h < active.Length; h++)
        {
            if (!active[h]) continue;
            var star = mesh.Stars[mesh.To(h)];
            int at = star.IndexOf(h ^ 1);
            for (int step = 1; step <= star.Count; step++)
            {
                int candidate = star[(at - step + star.Count) % star.Count];
                if (active[candidate]) { next[h] = candidate; break; }
            }
            if (next[h] < 0) throw new InvalidOperationException("A selected face boundary is open.");
        }
        var component = Enumerable.Repeat(-1, mesh.Points.Count).ToArray();
        int label = 0;
        for (int v = 0; v < component.Length; v++)
        {
            if (component[v] >= 0 || mesh.Stars[v].Count == 0) continue;
            var todo = new Stack<int>(); todo.Push(v); component[v] = label++;
            while (todo.Count > 0)
            {
                int p = todo.Pop();
                foreach (int h in mesh.Stars[p]) if (component[mesh.To(h)] < 0) { component[mesh.To(h)] = component[p]; todo.Push(mesh.To(h)); }
            }
        }
        var cycles = new List<Cycle>(); var visited = new bool[active.Length];
        for (int seed = 0; seed < active.Length; seed++)
        {
            if (!active[seed] || visited[seed]) continue;
            var walk = new List<int>(); int h = seed;
            do
            {
                if (visited[h]) throw new InvalidOperationException("Face successor is not a permutation.");
                visited[h] = true; walk.Add(h); h = next[h];
            } while (h != seed);
            // Articulation vertices can repeat on the exterior boundary. Split them into simple cycles.
            var path = new List<int>(); var position = new Dictionary<int, int>();
            foreach (int edge in walk)
            {
                position[mesh.From(edge)] = path.Count; path.Add(edge);
                if (!position.TryGetValue(mesh.To(edge), out int at)) continue;
                var ring = path.GetRange(at, path.Count - at);
                foreach (int old in ring) position.Remove(mesh.From(old));
                path.RemoveRange(at, path.Count - at);
                if (ring.Count < 3) throw new InvalidOperationException("A degenerate face remained after bridge removal.");
                var points = ring.Select(e => mesh.Points[mesh.From(e)]).ToList();
                double area = SignedArea(points);
                if (area == 0) throw new InvalidOperationException("A zero-area cycle has no bounded side.");
                var loop = new PlanLoop(mesh.Layer, Simplified(points, mesh.Tolerance), !ring.Any(e => mesh.Edges[e / 2].Inserted));
                cycles.Add(new Cycle(ring, loop, area, component[mesh.From(ring[0])]));
            }
            if (path.Count != 0) throw new InvalidOperationException("A face walk left unaccounted edges.");
        }
        return cycles;
    }

    private static List<Face> Regions(Mesh mesh, List<Cycle> cycles, bool separateComponents)
    {
        var outer = cycles.Where(c => c.Area > 0).ToList();
        var holes = outer.Select(_ => new List<PlanLoop>()).ToArray();
        if (separateComponents)
            for (int i = 0; i < outer.Count; i++) foreach (int h in outer[i].Halves) mesh.Owner[h] = i;
        foreach (var cycle in cycles.Where(c => c.Area < 0))
        {
            int h = cycle.Halves[0];
            var probe = Mean([mesh.Points[mesh.From(h)], mesh.Points[mesh.To(h)]]);
            var containing = Enumerable.Range(0, outer.Count)
                .Where(i => (!separateComponents || outer[i].Component != cycle.Component)
                    && LoopGeometry.PointInPolygon(probe, outer[i].Loop.Points)).ToList();
            if (containing.Count == 0) continue; // Unbounded face, not a hole.
            double smallest = containing.Min(i => outer[i].Area);
            var owners = containing.Where(i => outer[i].Area == smallest).ToList();
            if (owners.Count != 1) throw new InvalidOperationException("A hole has equally enclosing faces; containment is ambiguous.");
            int owner = owners[0]; holes[owner].Add(cycle.Loop);
            if (separateComponents) foreach (int e in cycle.Halves) mesh.Owner[e] = owner;
        }
        return outer.Select((c, i) => new Face(c.Loop, holes[i])).ToList();
    }

    private static List<IReadOnlyList<DxfPoint>> Chains(Mesh mesh)
    {
        var open = mesh.Adjacency.Select(es => es.Where(e => mesh.Cut[e]).ToList()).ToArray();
        bool Stops(int v) => open[v].Count != 2 || mesh.Stars[v].Count > 0;
        var used = new bool[mesh.Edges.Count]; var result = new List<IReadOnlyList<DxfPoint>>();
        for (int v = 0; v < open.Length; v++)
        {
            if (!Stops(v)) continue;
            foreach (int first in open[v])
            {
                if (used[first]) continue;
                int at = v, edge = first; var points = new List<DxfPoint> { mesh.Points[v] };
                while (true)
                {
                    used[edge] = true; at = mesh.Other(edge, at); points.Add(mesh.Points[at]);
                    if (Stops(at)) break;
                    edge = open[at].Single(e => !used[e]);
                }
                result.Add(points);
            }
        }
        return result;
    }

    private static List<DxfPoint> Simplified(List<DxfPoint> points, double tolerance)
    {
        // All decisions are cyclic and simultaneous. The old Simplify's sequential near-point pass
        // would otherwise depend on which half-edge happened to start the ring.
        var center = Mean(points);
        var local = points.Select(p => new DxfPoint((double)((decimal)p.X - (decimal)center.X), (double)((decimal)p.Y - (decimal)center.Y))).ToList();
        var keep = Enumerable.Range(0, points.Count).Where(i =>
        {
            var a = local[(i - 1 + local.Count) % local.Count]; var b = local[i]; var c = local[(i + 1) % local.Count];
            return LoopGeometry.Beyond(Math.Abs(Cross(Sub(b, a), Sub(c, b))) / Math.Max(a.DistanceTo(c), 1e-9), tolerance);
        }).ToList();
        if (keep.Count < 3) return points;
        var reduced = keep.Select(i => local[i]).ToList();
        // Exact cleanup only: the tolerance decision above already used Within/Beyond at a micron.
        var clean = LoopGeometry.Simplify(reduced, 0);
        return clean.Select(p => points[keep[reduced.IndexOf(p)]]).ToList();
    }

    private static DxfPoint Mean(IReadOnlyList<DxfPoint> points)
    {
        decimal x = 0, y = 0;
        foreach (var p in points) { x += (decimal)p.X; y += (decimal)p.Y; }
        return new DxfPoint((double)(x / points.Count), (double)(y / points.Count));
    }
    private static double SignedArea(IReadOnlyList<DxfPoint> points)
    {
        decimal x = (decimal)points[0].X, y = (decimal)points[0].Y, area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            area += ((decimal)a.X - x) * ((decimal)b.Y - y) - ((decimal)b.X - x) * ((decimal)a.Y - y);
        }
        return (double)(area / 2);
    }
    private static bool BoxesMeet(Span a, Span b, double margin)
        => Math.Max(a.A.X, a.B.X) + margin >= Math.Min(b.A.X, b.B.X) && Math.Max(b.A.X, b.B.X) + margin >= Math.Min(a.A.X, a.B.X)
        && Math.Max(a.A.Y, a.B.Y) + margin >= Math.Min(b.A.Y, b.B.Y) && Math.Max(b.A.Y, b.B.Y) + margin >= Math.Min(a.A.Y, a.B.Y);
    private static DxfPoint Sub(DxfPoint a, DxfPoint b) => new(a.X - b.X, a.Y - b.Y);
    private static DxfPoint Add(DxfPoint a, DxfPoint b) => new(a.X + b.X, a.Y + b.Y);
    private static double Cross(DxfPoint a, DxfPoint b) => a.X * b.Y - a.Y * b.X;
    private static double Dot(DxfPoint a, DxfPoint b) => a.X * b.X + a.Y * b.Y;
    private static double Parameter(DxfPoint p, Span s) => Dot(Sub(p, s.A), Sub(s.B, s.A)) / Dot(Sub(s.B, s.A), Sub(s.B, s.A));
    private static DxfPoint At(Span s, double t) => new(s.A.X + t * (s.B.X - s.A.X), s.A.Y + t * (s.B.Y - s.A.Y));
    private static bool Intersection(Span a, Span b, out DxfPoint point, out double t, out double u)
    {
        var r = Sub(a.B, a.A); var s = Sub(b.B, b.A); var d = Sub(b.A, a.A);
        double cross = Cross(r, s); point = default; t = u = 0;
        if (cross == 0) return false;
        t = Cross(d, s) / cross; u = Cross(d, r) / cross;
        if (!double.IsFinite(t) || !double.IsFinite(u)) return false;
        var pa = At(a, t); var pb = At(b, u);
        if (!double.IsFinite(pa.X) || !double.IsFinite(pa.Y) || !double.IsFinite(pb.X) || !double.IsFinite(pb.Y)
            || Math.Abs(pa.X) > 1e9 || Math.Abs(pa.Y) > 1e9 || Math.Abs(pb.X) > 1e9 || Math.Abs(pb.Y) > 1e9) return false;
        // Symmetric construction: exchanging the two input segments does not choose a different side.
        point = Mean([pa, pb]);
        return true;
    }
    private static bool Conflicts(Span a, Span b, bool allowSharedEnds)
    {
        if (!BoxesMeet(a, b, 0)) return false;
        bool shared = a.A == b.A || a.A == b.B || a.B == b.A || a.B == b.B;
        if (Intersection(a, b, out _, out double t, out double u))
            return t >= 0 && t <= 1 && u >= 0 && u <= 1 && !(allowSharedEnds && shared);
        if (Cross(Sub(b.A, a.A), Sub(a.B, a.A)) != 0) return false;
        double t0 = Parameter(b.A, a), t1 = Parameter(b.B, a);
        double overlap = Math.Min(1, Math.Max(t0, t1)) - Math.Max(0, Math.Min(t0, t1));
        return overlap > 0 || (overlap == 0 && !(allowSharedEnds && shared));
    }
}
