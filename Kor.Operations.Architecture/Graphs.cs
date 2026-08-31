// WHAT IS CONNECTED TO WHAT, AND WHAT TURNS INTO WHAT.
//
// A matrix answers "does A touch B" one cell at a time. It cannot show you a cluster, a hub, or a
// chain three steps long — those are shapes, and you only see a shape by drawing the thing as a
// graph. Two views, because the two questions are different:
//
//   RELATIONSHIPS  every project and every system it is tied to, laid out so that things which pull
//                  on each other end up near each other. Hubs get big and central; an isolated tool
//                  drifts to the edge. Duplication is drawn as its own kind of tie, so two projects
//                  holding copies of the same type are pulled together and you can SEE the pairing.
//
//   RECIPES        what a drawing turns into, step by step. Artefacts are things; readers, composers
//                  and writers are the operations between them. This is the pipeline as it actually
//                  runs, ranked left to right, rather than as anyone remembers it.
//
// LAYOUT IS COMPUTED HERE, not in the renderer, because Visio has no force-directed layout and a
// graph drawn in rows is not a graph. Everything is deterministic — positions start on a circle by
// index and the iteration count is fixed — so the committed model does not churn between runs and
// the freshness diff stays meaningful.

using System.Globalization;

namespace Kor.Operations.Architecture;

public static class GraphBuilder
{
    public static List<ArchGraph> Build(
        List<ArchProject> projects,
        List<ArchType> types,
        HashSet<(string From, string To)> mentions,
        List<ArchFormat> formats,
        List<ArchExternal> externals,
        List<ArchDuplicate> duplicates)
        => new()
        {
            Relationships(projects, externals, duplicates),
            Recipes(types, mentions, formats),
        };

    // ---------------------------------------------------------------------------------------
    // 1. THE WEB
    // ---------------------------------------------------------------------------------------

    private static ArchGraph Relationships(
        List<ArchProject> projects, List<ArchExternal> externals, List<ArchDuplicate> duplicates)
    {
        var nodes = new List<(string Id, string Label, string Detail, string Group, double Weight)>();
        foreach (var p in projects)
            nodes.Add((p.Name, Short(p.Name), $"{p.Lines.ToString("N0", CultureInfo.InvariantCulture)} lines", p.Cluster, Math.Max(1, p.Lines)));
        foreach (var e in externals)
            nodes.Add(("ext:" + e.Name, e.Name, e.Kind, "external", Math.Max(1, e.Evidence.Count) * 400.0));

        var index = nodes.Select((n, i) => (n.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var edges = new List<ArchGraphEdge>();

        foreach (var p in projects)
            foreach (string r in p.ProjectRefs)
                if (index.ContainsKey(p.Name) && index.ContainsKey(r))
                    edges.Add(new ArchGraphEdge(p.Name, r, "references"));

        // Which project each external is reached from. Longest directory first, so a file lands in
        // its nearest project rather than an ancestor that happens to share a prefix.
        var byDir = projects.OrderByDescending(p => p.Dir.Length).ToList();
        foreach (var e in externals)
            foreach (string owner in e.Evidence
                         .Select(ev => byDir.FirstOrDefault(p => ev.StartsWith(p.Dir + "/", StringComparison.OrdinalIgnoreCase))?.Name)
                         .Where(n => n is not null)
                         .Distinct(StringComparer.Ordinal)!)
                edges.Add(new ArchGraphEdge(owner, "ext:" + e.Name, "talks to"));

        // A DUPLICATION TIE. Two projects declaring the same type name are pulled together, so the
        // pairs that share several names sit as a knot rather than as two rows on a list.
        var boilerplate = new HashSet<string>(StringComparer.Ordinal)
            { "Program", "ToolOptions", "ImportOptions", "ImportStats", "ImportConfig", "Options", "Result" };
        var dupPairs = new Dictionary<(string, string), int>();
        foreach (var d in duplicates)
        {
            if (boilerplate.Contains(d.Name)) continue;
            var ps = d.Projects.Where(index.ContainsKey).OrderBy(x => x, StringComparer.Ordinal).ToList();
            for (int i = 0; i < ps.Count; i++)
                for (int j = i + 1; j < ps.Count; j++)
                {
                    var key = (ps[i], ps[j]);
                    dupPairs[key] = dupPairs.TryGetValue(key, out int n) ? n + 1 : 1;
                }
        }
        foreach (var ((a, b), n) in dupPairs
                     .OrderBy(k => k.Key.Item1, StringComparer.Ordinal)
                     .ThenBy(k => k.Key.Item2, StringComparer.Ordinal))
            edges.Add(new ArchGraphEdge(a, b, "duplicates:" + n.ToString(CultureInfo.InvariantCulture)));

        // PROJECTS AND EXTERNALS ARE SIZED ON SEPARATE SCALES. Sharing one made Deltek — named in
        // 178 files — the biggest thing on the page, which says only that a connection string is
        // mentioned a lot. A project's size means lines of code; an external's means how much of the
        // repo touches it, and the two are not the same quantity.
        double maxProject = Math.Max(1, nodes.Where(n => n.Group != "external").Max(n => n.Weight));
        double maxExternal = Math.Max(1, nodes.Where(n => n.Group == "external").Select(n => n.Weight).DefaultIfEmpty(1).Max());

        var sized = nodes.Select(n => n.Group == "external"
            ? 0.16 + 0.30 * Math.Sqrt(n.Weight / maxExternal)
            : Math.Sqrt(n.Weight / maxProject)).ToList();

        var pairs = edges
            .Where(e => index.ContainsKey(e.From) && index.ContainsKey(e.To))
            .Select(e => (A: index[e.From], B: index[e.To]))
            .ToList();

        // A NODE WITH NO TIES IS NOT PART OF A RELATIONSHIP GRAPH, and leaving it in the physics
        // wrecks the drawing: nothing pulls it back, so it drifts out until repulsion balances
        // gravity, and normalising to the extremes then squeezes everything that DOES relate into a
        // knot half an inch across. Fourteen of the one-off tools reference nothing at all.
        //
        // So they are parked in a labelled strip along the bottom instead of being scattered by a
        // simulation that has nothing to say about them. That they are unattached is itself worth
        // seeing — it is just not worth eighty per cent of the sheet.
        var degree = new int[nodes.Count];
        foreach (var (a, b) in pairs) { degree[a]++; degree[b]++; }

        var tied = Enumerable.Range(0, nodes.Count).Where(i => degree[i] > 0).ToList();
        var loose = Enumerable.Range(0, nodes.Count).Where(i => degree[i] == 0).ToList();
        var slot = tied.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);

        var tiedPos = ForceDirected(
            tied.Count,
            pairs.Select(p => (slot[p.A], slot[p.B])).ToList(),
            tied.Select(i => sized[i]).ToList());

        var pos = new (double X, double Y)[nodes.Count];
        foreach (int i in tied)
        {
            var q = tiedPos[slot[i]];
            pos[i] = (q.X, 0.16 + q.Y * 0.84);         // the connected graph gets the top 84%
        }
        for (int i = 0; i < loose.Count; i++)
        {
            int perRow = Math.Max(1, (int)Math.Ceiling(loose.Count / 2.0));
            int row = i / perRow, col = i % perRow;
            pos[loose[i]] = (perRow == 1 ? 0.5 : 0.03 + (col / (perRow - 1.0)) * 0.94,
                             row == 0 ? 0.075 : 0.015);
        }

        var outNodes = nodes.Select((n, i) => new ArchNode(
            n.Id, n.Label, n.Detail, n.Group,
            Round(degree[i] == 0 ? Math.Min(sized[i], 0.22) : sized[i]),   // area ∝ size, so a 92k-line project is not 92x wide
            Round(pos[i].X), Round(pos[i].Y))).ToList();

        return new ArchGraph(
            "Relationships",
            "Everything, and what it is tied to",
            $"{outNodes.Count} nodes, {edges.Count} ties. Grey = one project references another. " +
            "Yellow = it talks to something outside the repo. RED = the two hold types of the same name, " +
            "and the number is how many. Position is force-directed: what pulls together sits together. " +
            $"The {loose.Count} along the bottom are tied to NOTHING — they reference no project and reach " +
            "nothing outside the repo.",
            outNodes,
            edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal).ToList());
    }

    // ---------------------------------------------------------------------------------------
    // 2. THE RECIPE
    // ---------------------------------------------------------------------------------------

    private static ArchGraph Recipes(
        List<ArchType> types, HashSet<(string From, string To)> mentions, List<ArchFormat> formats)
    {
        var spine = types
            .Where(t => t.Namespace.Contains("EngineeringTools", StringComparison.Ordinal))
            .Where(t => t.Role is "read" or "compose" or "classify" or "write")
            .ToList();
        var spineById = spine.ToDictionary(t => t.Id, StringComparer.Ordinal);

        var nodes = new List<(string Id, string Label, string Detail, string Group, int Rank)>();

        var readExt = formats.Where(f => spineById.TryGetValue(f.Type, out var t) && t.Role == "read")
            .Select(f => f.Ext).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var writeExt = formats.Where(f => spineById.TryGetValue(f.Type, out var t) && t.Role == "write")
            .Select(f => f.Ext).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        foreach (string e in readExt) nodes.Add(("in:" + e, e, "arrives as", "artefact", 0));
        foreach (var t in spine.Where(t => t.Role == "read").DistinctBy(t => t.Name).OrderBy(t => t.Name, StringComparer.Ordinal))
            nodes.Add((t.Id, t.Name, "reads", "read", 1));
        foreach (var t in spine.Where(t => t.Role is "compose" or "classify").DistinctBy(t => t.Name).OrderBy(t => t.Name, StringComparer.Ordinal))
            nodes.Add((t.Id, t.Name, t.Role, t.Role, 2));
        foreach (var t in spine.Where(t => t.Role == "write").DistinctBy(t => t.Name).OrderBy(t => t.Name, StringComparer.Ordinal))
            nodes.Add((t.Id, t.Name, "writes", "write", 3));
        foreach (string e in writeExt) nodes.Add(("out:" + e, e, "ships as", "artefact", 4));

        var byId = nodes.Select((n, i) => (n.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var edges = new List<ArchGraphEdge>();

        foreach (var f in formats)
        {
            if (!spineById.TryGetValue(f.Type, out var t)) continue;
            if (t.Role == "read" && byId.ContainsKey("in:" + f.Ext) && byId.ContainsKey(t.Id))
                edges.Add(new ArchGraphEdge("in:" + f.Ext, t.Id, "into"));
            if (t.Role == "write" && byId.ContainsKey(t.Id) && byId.ContainsKey("out:" + f.Ext))
                edges.Add(new ArchGraphEdge(t.Id, "out:" + f.Ext, "into"));
        }

        foreach (var (from, to) in mentions
                     .OrderBy(m => m.From, StringComparer.Ordinal)
                     .ThenBy(m => m.To, StringComparer.Ordinal))
        {
            if (!byId.ContainsKey(from) || !byId.ContainsKey(to)) continue;
            int rf = nodes[byId[from]].Rank, rt = nodes[byId[to]].Rank;
            // A mention points at what is USED, so the flow runs the other way: a composer naming a
            // reader means the reader feeds the composer.
            if (rf > rt) edges.Add(new ArchGraphEdge(to, from, "feeds"));
            else if (rf < rt) edges.Add(new ArchGraphEdge(from, to, "feeds"));
            else edges.Add(new ArchGraphEdge(from, to, "same rank"));
        }

        edges = edges.DistinctBy(e => (e.From, e.To, e.Kind)).ToList();
        var pos = Layered(nodes.Select(n => n.Rank).ToList(),
                          edges.Where(e => byId.ContainsKey(e.From) && byId.ContainsKey(e.To))
                               .Select(e => (byId[e.From], byId[e.To])).ToList());

        var outNodes = nodes.Select((n, i) => new ArchNode(
            n.Id, n.Label, n.Detail, n.Group, 1.0, Round(pos[i].X), Round(pos[i].Y))).ToList();

        return new ArchGraph(
            "Recipes",
            "What a drawing turns into",
            $"{outNodes.Count} steps, {edges.Count} arrows. Rectangles are ARTEFACTS — a file you can " +
            "hold. Diamonds are OPERATIONS. Read left to right: what arrives, what reads it, what holds " +
            "it, what writes it, what ships. Ordered to cross as little as possible, not by name.",
            outNodes,
            edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal).ToList());
    }

    // ---------------------------------------------------------------------------------------
    // LAYOUT
    // ---------------------------------------------------------------------------------------

    /// <summary>Fruchterman-Reingold with gravity. Every node pushes every other away; every edge
    /// pulls its two together; a weak pull to the centre keeps the whole thing on the page; and it
    /// cools.
    ///
    /// GRAVITY IS NOT OPTIONAL HERE. Without it, repulsion is the only force acting on a node that
    /// has no edges, so it accelerates away forever — and this repository has thirty-four one-off
    /// tools that reference nothing. The first render put them in the four corners of a 44-inch
    /// sheet and squeezed everything that actually relates to anything into a knot half an inch
    /// across, because normalising to the extremes scales to the outliers.
    ///
    /// Repulsion is scaled by node size too, or the big nodes overlap: pushing a 2.4-inch circle
    /// with the same force as a 0.3-inch one leaves the large ones sitting on top of each other.
    ///
    /// Starting positions are a circle taken in index order and the iteration count is fixed, so the
    /// same input always gives the same picture — a layout seeded from a clock would rewrite the
    /// committed model on every run and make its diff worthless.</summary>
    private static (double X, double Y)[] ForceDirected(
        int count, List<(int A, int B)> edges, IReadOnlyList<double>? sizes = null)
    {
        var p = new (double X, double Y)[count];
        for (int i = 0; i < count; i++)
        {
            double a = 2.0 * Math.PI * i / Math.Max(1, count);
            p[i] = (0.5 + 0.45 * Math.Cos(a), 0.5 + 0.45 * Math.Sin(a));
        }
        if (count < 2) return p;

        double k = Math.Sqrt(1.0 / count);
        double temp = 0.14;
        const int iterations = 700;
        const double gravity = 0.055;

        var disp = new (double X, double Y)[count];
        for (int step = 0; step < iterations; step++)
        {
            Array.Clear(disp);

            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    double dx = p[i].X - p[j].X, dy = p[i].Y - p[j].Y;
                    double d = Math.Max(1e-4, Math.Sqrt(dx * dx + dy * dy));
                    double bulk = sizes is null ? 1.0 : 1.0 + (sizes[i] + sizes[j]);
                    double force = (k * k) / d * bulk;
                    double ux = dx / d * force, uy = dy / d * force;
                    disp[i] = (disp[i].X + ux, disp[i].Y + uy);
                    disp[j] = (disp[j].X - ux, disp[j].Y - uy);
                }

            foreach (var (a, b) in edges)
            {
                if (a == b) continue;
                double dx = p[a].X - p[b].X, dy = p[a].Y - p[b].Y;
                double d = Math.Max(1e-4, Math.Sqrt(dx * dx + dy * dy));
                double force = (d * d) / k;
                double ux = dx / d * force, uy = dy / d * force;
                disp[a] = (disp[a].X - ux, disp[a].Y - uy);
                disp[b] = (disp[b].X + ux, disp[b].Y + uy);
            }

            for (int i = 0; i < count; i++)
            {
                disp[i] = (disp[i].X + (0.5 - p[i].X) * gravity,
                           disp[i].Y + (0.5 - p[i].Y) * gravity);

                double d = Math.Max(1e-4, Math.Sqrt(disp[i].X * disp[i].X + disp[i].Y * disp[i].Y));
                double lim = Math.Min(d, temp);
                p[i] = (p[i].X + disp[i].X / d * lim, p[i].Y + disp[i].Y / d * lim);
            }
            temp *= 0.992;
        }

        return Normalise(p);
    }

    /// <summary>Rank fixes X; within a rank, each node slides to the average position of what it is
    /// joined to, swept back and forth. That is the barycentre method, and it is what turns a
    /// spaghetti of arrows into something that reads left to right.</summary>
    private static (double X, double Y)[] Layered(List<int> ranks, List<(int A, int B)> edges)
    {
        int count = ranks.Count;

        // AN EMPTY GRAPH IS A GRAPH. `ranks.Max()` below throws on no elements, so extracting any
        // repository with no drawing-intake types at all — which is every repository except this one
        // — crashed the whole extraction, not just the page that would have been blank. Found by the
        // synthetic fixtures written for the audit fixes; the audit itself never saw it, because it
        // only ever ran against a tree that happens to have those types in it.
        if (count == 0) return Array.Empty<(double X, double Y)>();

        var order = new double[count];
        var byRank = ranks.Select((r, i) => (r, i)).GroupBy(x => x.r)
            .ToDictionary(g => g.Key, g => g.Select(x => x.i).ToList());

        foreach (var (_, members) in byRank)
            for (int i = 0; i < members.Count; i++)
                order[members[i]] = i;

        var neighbours = new List<int>[count];
        for (int i = 0; i < count; i++) neighbours[i] = new List<int>();
        foreach (var (a, b) in edges) { neighbours[a].Add(b); neighbours[b].Add(a); }

        for (int sweep = 0; sweep < 40; sweep++)
            foreach (int rank in byRank.Keys.OrderBy(r => sweep % 2 == 0 ? r : -r))
            {
                var members = byRank[rank];
                var scored = members
                    .Select(i => (i, key: neighbours[i].Count == 0 ? order[i] : neighbours[i].Average(n => order[n])))
                    .OrderBy(x => x.key).ThenBy(x => x.i)
                    .ToList();
                for (int i = 0; i < scored.Count; i++) order[scored[i].i] = i;
            }

        var p = new (double X, double Y)[count];
        int maxRank = Math.Max(1, ranks.Max());
        foreach (var (rank, members) in byRank)
        {
            int n = Math.Max(1, members.Count);
            foreach (int i in members)
                p[i] = ((double)rank / maxRank, n == 1 ? 0.5 : order[i] / (n - 1.0));
        }
        return p;
    }

    private static (double X, double Y)[] Normalise((double X, double Y)[] p)
    {
        if (p.Length == 0) return p;        // Min/Max throw on empty — same trap as Layered
        double x0 = p.Min(q => q.X), x1 = p.Max(q => q.X);
        double y0 = p.Min(q => q.Y), y1 = p.Max(q => q.Y);
        double w = Math.Max(1e-6, x1 - x0), h = Math.Max(1e-6, y1 - y0);
        for (int i = 0; i < p.Length; i++) p[i] = ((p[i].X - x0) / w, (p[i].Y - y0) / h);
        return p;
    }

    /// <summary>Four decimals. Enough to place a node on a sixty-inch sheet, few enough that a
    /// rounding wobble in the last bit does not show up as a change in the committed model.</summary>
    private static double Round(double v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

    private static string Short(string n)
        => n.StartsWith("Kor.Operations.", StringComparison.Ordinal) ? n["Kor.Operations.".Length..]
         : n.StartsWith("Kor.Opportunities.", StringComparison.Ordinal) ? n["Kor.Opportunities.".Length..]
         : n.StartsWith("Kor.", StringComparison.Ordinal) ? n["Kor.".Length..]
         : n;
}
