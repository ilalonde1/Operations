// The takeoff verb `dxf-inspect`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
internal static class DxfInspectVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("dxf-inspect", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff dxf-inspect <plan.dxf> [--walls] [--columns] [--plates] [--loops] [--members]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Not found '{args[1]}'."); return 2; }

        bool wallDetail = args.Any(a => a.Equals("--walls", StringComparison.OrdinalIgnoreCase));
        bool columnDetail = args.Any(a => a.Equals("--columns", StringComparison.OrdinalIgnoreCase));
        bool plateDetail = args.Any(a => a.Equals("--plates", StringComparison.OrdinalIgnoreCase));
        bool loopDetail = args.Any(a => a.Equals("--loops", StringComparison.OrdinalIgnoreCase));
        bool memberDetail = args.Any(a => a.Equals("--members", StringComparison.OrdinalIgnoreCase));
        // the standing rules are stated in inches; the sheet is read in its own unit (a millimetre PDF view read at
        // inch thresholds classified nothing - every loop "-> 0 panel(s)" - until 2026-09-13)
        var inspectOptions = new PlanClassificationOptions().InUnitOf(DxfPlanReader.UnitInInches(args[1]) ?? 1.0);
        var inspectSegments = DxfPlanReader.ReadSegments(args[1]);

        if (columnDetail)
        {
            WriteColumns(args[1], StructuralPlanClassifier.Classify(inspectSegments, inspectOptions));
            return 0;
        }

        // WHICH LOOPS HAVE NO HONEST CENTRE? A bow-tie's area formula puts its centre kilometres
        // away (31202, 2026-09-12); a loop drawn out-and-back has no area at all. PlanLoop.Centroid
        // falls back to the vertex mean for both, and this lists every wall and column loop on the
        // sheet - the loops the classifier itself builds, not a copy - where the two answers differ
        // by more than a millimetre: the loops that fallback changed, with their vertices.
        if (loopDetail)
        {
            Console.WriteLine($"{Path.GetFileName(args[1])}");
            int loops = 0, changed = 0;
            foreach (var (role, loop) in StructuralPlanClassifier.WallAndColumnLoops(inspectSegments, inspectOptions))
            {
                loops++;
                var (minX, minY, maxX, maxY) = loop.Bounds();
                double box = Math.Max((maxX - minX) * (maxY - minY), 1e-9);
                double sa = loop.SignedArea;
                double cx = 0, cy = 0;
                for (int i = 0; i < loop.Points.Count; i++)
                {
                    var p = loop.Points[i];
                    var q = loop.Points[(i + 1) % loop.Points.Count];
                    double cross = p.X * q.Y - q.X * p.Y;
                    cx += (p.X + q.X) * cross;
                    cy += (p.Y + q.Y) * cross;
                }
                double fx = sa == 0 ? double.NaN : cx / (6.0 * sa), fy = sa == 0 ? double.NaN : cy / (6.0 * sa);
                var c = loop.Centroid();
                bool differs = double.IsNaN(fx) || Math.Abs(fx - c.X) > 1 || Math.Abs(fy - c.Y) > 1;
                if (!differs) continue;
                changed++;
                string why = Math.Abs(sa) < 1e-3 * box ? $"area {sa:0.###} is under a thousandth of its {box:0} box" : "formula centre outside the box";
                Console.WriteLine($"  {role,-8} {loop.Layer,-16} {loop.Points.Count,3} pts  box {maxX - minX:0}x{maxY - minY:0} at {minX:0},{minY:0}  formula ({fx:0.#}, {fy:0.#}) -> ({c.X:0.#}, {c.Y:0.#})  {why}");
                Console.WriteLine("           " + string.Join(" ", loop.Points.Select(p => $"({p.X:0.#},{p.Y:0.#})")));
            }
            Console.WriteLine($"{changed} of {loops} wall and column loops take the vertex mean");
            return 0;
        }

        // WHAT THE READER MADE OF THIS SHEET: every wall (axis ends, thickness) and column (centre, size) the
        // classifier hands the composer, in the sheet's own frame and unit, one per line, sorted - so two readings
        // of one sheet (a reissue; the same sheet shifted on the page, intake step 56) diff line for line. Read
        // without the sheet's words and tags, as MemberPoints is.
        if (memberDetail)
        {
            var made = StructuralPlanClassifier.Classify(inspectSegments, inspectOptions);
            Console.WriteLine($"{Path.GetFileName(args[1])}: {made.Walls.Count} walls, {made.Columns.Count} columns, {made.WallOpenings.Count} wall openings");
            var lines = made.Walls.Select(w =>
                {
                    var (a, b) = (w.Start.X, w.Start.Y).CompareTo((w.End.X, w.End.Y)) <= 0 ? (w.Start, w.End) : (w.End, w.Start);
                    return $"  wall   ({a.X:0},{a.Y:0})-({b.X:0},{b.Y:0})  t {w.Thickness:0}  {w.Layer}";
                })
                .Concat(made.Columns.Select(c => $"  column ({c.Center.X:0},{c.Center.Y:0})  {c.Width:0}x{c.Depth:0}{(c.IsRound ? " round" : "")}{(c.FromBelow ? " below" : "")}  {c.Layer}"))
                .Concat(made.WallOpenings.Select(o => $"  opening ({o.Start.X:0},{o.Start.Y:0})-({o.End.X:0},{o.End.Y:0})  t {o.Thickness:0}"))
                .OrderBy(l => l, StringComparer.Ordinal);
            foreach (string line in lines) Console.WriteLine(line);
            return 0;
        }

        // What floor plates does this ONE sheet yield, and at what bridge width? Reading it out of a
        // finished model means rebuilding the whole job over SMB to answer a question about one
        // storey. This answers it in a second, which is the difference between measuring and guessing.
        if (plateDetail)
        {
            var onlyPlateLayers = new List<string>();
            for (int i = 2; i < args.Length - 1; i++)
                if (args[i].Equals("--layers", StringComparison.OrdinalIgnoreCase))
                    onlyPlateLayers.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));

            if (onlyPlateLayers.Count > 0)
                inspectSegments = inspectSegments
                    .Where(x => onlyPlateLayers.Any(l => x.Layer.Equals(l, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

            Console.WriteLine($"{Path.GetFileName(args[1])}" +
                (onlyPlateLayers.Count > 0 ? $"   [{string.Join(", ", onlyPlateLayers)} only, {inspectSegments.Count} segs]" : ""));
            double banked = inspectOptions.FloodFillBridge;
            foreach (double bridge in new[] { banked, banked * 1.5, banked * 2.0, banked * 2.5, banked * 3.0, banked * 4.0 })
            {
                var got = StructuralPlanClassifier.Classify(inspectSegments, inspectOptions with { FloodFillBridge = bridge });
                Console.WriteLine($"  bridge {bridge,6:0.#} in   {got.Slabs.Count,2} plate(s)");
                foreach (var plate in got.Slabs.OrderByDescending(x => x.Area))
                {
                    double x0 = plate.Points.Min(q => q.X) / 12, x1 = plate.Points.Max(q => q.X) / 12;
                    double y0 = plate.Points.Min(q => q.Y) / 12, y1 = plate.Points.Max(q => q.Y) / 12;
                    Console.WriteLine($"      {plate.Area / 144,10:N0} sq ft   x {x0,7:0}..{x1,-7:0} y {y0,7:0}..{y1,-7:0}");
                }
            }
            return 0;
        }

        // WHY DOES THIS PAGE'S EDGE NOT CLOSE? (step 78, 2026-09-15) The faces PlanarRings finds in the named
        // layers' lines at the PDF route's own tolerances, largest first, with what stands in each; then the
        // longest open chains with their ends and the nearest other loose end - the gap the drawing leaves.
        if (args.Any(a => a.Equals("--faces", StringComparison.OrdinalIgnoreCase)))
        {
            var faceLayers = new List<string>();
            for (int i = 2; i < args.Length - 1; i++)
                if (args[i].Equals("--layers", StringComparison.OrdinalIgnoreCase))
                    faceLayers.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            double unitInInches = DxfPlanReader.UnitInInches(args[1]) ?? 1.0;
            double mm = 1.0 / 25.4 / unitInInches;   // one millimetre in the drawing's unit
            var lines = inspectSegments.Where(s => faceLayers.Count == 0 || faceLayers.Any(l => s.Layer.Equals(l, StringComparison.OrdinalIgnoreCase))).ToList();
            var memberSegments = inspectSegments.Where(s => PlanClassificationOptions.Matches(s.Layer, inspectOptions.WallLayerPatterns) || PlanClassificationOptions.Matches(s.Layer, inspectOptions.ColumnLayerPatterns)).ToList();
            var standing = memberSegments.Select(s => s.Start).ToList();
            // an edge interrupted in line is one edge (step 78): the same bridging the reader applies, at the same limit
            var memberOutlines = new PlanLoopBuilder(inspectOptions.JoinTolerance, inspectOptions.JoinTolerance, inspectOptions.JoinTolerance).Build(memberSegments).Loops.Select(l => l.Points).ToList();
            // the reader's own policy: exact joins first, then only chains long enough to be a piece of an edge (2 m) are
            // bridged and arranged - a hatch of dashes is neither - with the closed loops' segments beside them
            int drawn = lines.Count;
            var exact = new PlanLoopBuilder(GeometryFilterService.SlabEdgeJoinMm * mm, GeometryFilterService.SlabEdgeJoinMm * mm, GeometryFilterService.SlabEdgeJoinMm * mm).Build(lines);
            lines = new List<DxfSegment>();
            foreach (var c in exact.OpenChains.Where(c => c.Count >= 2 && Enumerable.Range(0, c.Count - 1).Sum(i => c[i].DistanceTo(c[i + 1])) >= GeometryFilterService.SlabEdgeChainMinMm * mm))
                for (int i = 0; i + 1 < c.Count; i++) lines.Add(new DxfSegment("SLABEDGE", c[i], c[i + 1]));
            var inLine = GeometryFilterService.BridgesInLine(lines, GeometryFilterService.SlabEdgeExtendMm * mm, GeometryFilterService.SlabEdgeJoinMm * mm);
            lines.AddRange(inLine);
            foreach (var l in exact.Loops)
                for (int i = 0; i < l.Points.Count; i++) lines.Add(new DxfSegment("SLABEDGE", l.Points[i], l.Points[(i + 1) % l.Points.Count]));
            Console.WriteLine($"{Path.GetFileName(args[1])}   {drawn} lines on [{(faceLayers.Count > 0 ? string.Join(", ", faceLayers) : "every layer")}]: {exact.Loops.Count} closed by exact joins, {exact.OpenChains.Count} open chains of which the pieces of 2 m or more give {lines.Count - inLine.Count - exact.Loops.Sum(l => l.Points.Count)} segments + {inLine.Count} across gaps in line; join {GeometryFilterService.SlabEdgeJoinMm * mm:0.###} bridge {GeometryFilterService.DefaultSlabEdgeBridgeMm * mm:0.#} extend {GeometryFilterService.SlabEdgeExtendMm * mm:0.#} (drawing units)");
            PlanarRings.Result faces;
            try { faces = new PlanarRings(GeometryFilterService.SlabEdgeJoinMm * mm, GeometryFilterService.DefaultSlabEdgeBridgeMm * mm, GeometryFilterService.SlabEdgeExtendMm * mm).Build(lines); }
            catch (InvalidOperationException e) { Console.WriteLine($"  arrangement REFUSED: {e.Message}"); return 3; }
            double sqFt = unitInInches * unitInInches / 144.0;
            double minPlate = GeometryFilterService.DefaultMinSlabAreaMm2 * mm * mm;
            List<PlanarRings.Face> united;
            try { united = faces.RecoverSurfaces(_ => false, cell => standing.Any(q => LoopGeometry.PointInPolygon(q, cell.Outer.Points))).Slabs.Where(u => u.Outer.Area >= minPlate).ToList(); }
            catch (InvalidOperationException e) { Console.WriteLine($"  union REFUSED: {e.Message}"); united = []; }
            Console.WriteLine($"  {faces.Faces.Count} bounded face(s), {faces.OpenChains.Count} open chain(s); cells with structure in them unite into {united.Count} plate(s) of {minPlate * sqFt:N0} sq ft or more:" +
                string.Concat(united.OrderByDescending(u => u.Outer.Area).Take(6).Select(u => $" {u.Outer.Area * sqFt:N0} sq ft ({u.Holes.Count} hole(s))")));
            foreach (var f in faces.Faces.OrderByDescending(f => f.Outer.Area).Take(12))
            {
                var p = f.Outer.Points;
                int inside = standing.Count(q => LoopGeometry.PointInPolygon(q, p));
                Console.WriteLine($"    face {f.Outer.Area * sqFt,10:N0} sq ft  {p.Count,4} pts  holes {f.Holes.Count,2}  x {p.Min(q => q.X) * unitInInches / 12,7:0}..{p.Max(q => q.X) * unitInInches / 12,-7:0} y {p.Min(q => q.Y) * unitInInches / 12,7:0}..{p.Max(q => q.Y) * unitInInches / 12,-7:0} ft   wall/column points inside {inside}");
            }
            var chains = faces.OpenChains.Select(c => (Chain: c, Length: Enumerable.Range(0, c.Count - 1).Sum(i => c[i].DistanceTo(c[i + 1])))).OrderByDescending(c => c.Length).ToList();
            var ends = faces.OpenChains.SelectMany(c => new[] { c[0], c[^1] }).ToList();
            foreach (var (chain, length) in chains.Take(12))
            {
                string End(DxfPoint e)
                {
                    double best = ends.Where(o => o != e).Select(o => o.DistanceTo(e)).DefaultIfEmpty(double.NaN).Min();
                    // the member beside this end, if one stands within four feet: its box and how far the end is from it
                    string beside = "";
                    foreach (var m in memberOutlines)
                    {
                        double d = Enumerable.Range(0, m.Count).Min(i => LoopGeometry.DistanceToSegment(e, m[i], m[(i + 1) % m.Count]));
                        if (d * unitInInches > 48) continue;
                        beside = $" member {(m.Max(p => p.X) - m.Min(p => p.X)) * unitInInches:0}x{(m.Max(p => p.Y) - m.Min(p => p.Y)) * unitInInches:0} in at x {m.Min(p => p.X) * unitInInches / 12:0.0}..{m.Max(p => p.X) * unitInInches / 12:0.0} y {m.Min(p => p.Y) * unitInInches / 12:0.0}..{m.Max(p => p.Y) * unitInInches / 12:0.0}, {d * unitInInches:0.0} in off";
                        break;
                    }
                    return $"({e.X * unitInInches / 12:0.0},{e.Y * unitInInches / 12:0.0}) nearest loose end {best * unitInInches:0} in{beside}";
                }
                Console.WriteLine($"    chain {length * unitInInches / 12,8:0.0} ft  {chain.Count,4} pts   {End(chain[0])}  ..  {End(chain[^1])}");
            }
            return 0;
        }

        Console.WriteLine($"{Path.GetFileName(args[1])}");
        Console.WriteLine($"segments: {inspectSegments.Count}");
        Console.WriteLine();

        foreach (var layerGroup in inspectSegments.GroupBy(s => s.Layer).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var built = new PlanLoopBuilder(inspectOptions.JoinTolerance, inspectOptions.BridgeTolerance).Build(layerGroup);
            Console.WriteLine($"{layerGroup.Key,-24} segs {layerGroup.Count(),4}   closed loops {built.Loops.Count,3}   open {built.OpenChains.Count,3}");

            // How far is each loose end from the nearest other loose end? The measured answer to
            // whether an outline fails on tolerance or on something else entirely.
            if (built.OpenChains.Count > 0)
            {
                var ends = built.OpenChains.SelectMany(c => new[] { c[0], c[^1] }).ToList();
                var nearest = new List<double>();
                for (int a = 0; a < ends.Count; a++)
                {
                    double best = double.MaxValue;
                    for (int b = 0; b < ends.Count; b++)
                    {
                        if (a / 2 == b / 2) continue;   // the other end of the same chain does not count
                        double dist = ends[a].DistanceTo(ends[b]);
                        if (dist < best) best = dist;
                    }
                    if (best < double.MaxValue) nearest.Add(best);
                }
                nearest.Sort();
                if (nearest.Count > 0)
                {
                    string Bucket(double limit) => $"{nearest.Count(v => v <= limit)}/{nearest.Count}";
                    Console.WriteLine($"    loose ends {nearest.Count,3}   gap median {nearest[nearest.Count / 2],7:0.00}  max {nearest[^1],8:0.0}   " +
                        $"<=0.1 {Bucket(0.1)}  <=1 {Bucket(1)}  <=6 {Bucket(6)}  <=24 {Bucket(24)}");
                }
            }

            if (!wallDetail || !PlanClassificationOptions.Matches(layerGroup.Key, inspectOptions.WallLayerPatterns)) continue;

            foreach (var loop in built.Loops.OrderByDescending(l => l.Area))
            {
                var lb = LoopGeometry.MinAreaBox(loop.Points);
                var panels = WallOutlineDecomposer.Decompose(loop, inspectOptions);
                var copy = WallOutlineDecomposer.Decompose(
                    new PlanLoop(loop.Layer, loop.Points.ToList(), true), inspectOptions);
                Console.WriteLine($"    loop: {loop.Points.Count,3} vertices, box {lb.Length:0}x{lb.Thickness:0}, area {loop.Area:0} -> {panels.Count} panel(s) [exact={loop.ClosedExactly}, copy={copy.Count}]");
                Console.WriteLine("        pts " + string.Join(" ", loop.Points.Select(p => $"({p.X:R},{p.Y:R})")));
                foreach (var p in panels.OrderByDescending(p => p.Length).Take(12))
                    Console.WriteLine($"        {p.Length,7:0.0} long x {p.Thickness,5:0.0} thick");
            }

            // How far is each loose end from the nearest other loose end? That is the measured
            // answer to whether these outlines fail on tolerance or on something else.
            if (built.OpenChains.Count > 0)
            {
                var ends = built.OpenChains.SelectMany(c => new[] { c[0], c[^1] }).ToList();
                var nearest = new List<double>();
                for (int a = 0; a < ends.Count; a++)
                {
                    double best = double.MaxValue;
                    for (int b = 0; b < ends.Count; b++)
                    {
                        if (a / 2 == b / 2) continue;   // ignore the other end of the same chain
                        double dist = ends[a].DistanceTo(ends[b]);
                        if (dist < best) best = dist;
                    }
                    if (best < double.MaxValue) nearest.Add(best);
                }
                nearest.Sort();
                if (nearest.Count > 0)
                {
                    string Bucket(double limit) => $"{nearest.Count(d => d <= limit)}/{nearest.Count}";
                    Console.WriteLine($"    loose ends: {nearest.Count}   nearest-neighbour gap: " +
                        $"median {nearest[nearest.Count / 2]:0.00}, max {nearest[^1]:0.0}   " +
                        $"within 0.1={Bucket(0.1)} 1={Bucket(1)} 6={Bucket(6)} 24={Bucket(24)}");
                }
            }

            foreach (var chain in built.OpenChains.Take(3))
            {
                Console.WriteLine($"    OPEN chain: {chain.Count} pts, gap {chain[0].DistanceTo(chain[^1]):0.0}");
            }
        }
        return 0;
    }

    /// <summary>The same per-sheet classification and listing used by corpus-query columns; no PDF or model is written.</summary>
    public static IReadOnlyList<(string Branch, bool InsideWall)> InspectColumns(string path)
    {
        var options = new PlanClassificationOptions().InUnitOf(DxfPlanReader.UnitInInches(path) ?? 1.0);
        return WriteColumns(path, StructuralPlanClassifier.Classify(DxfPlanReader.ReadSegments(path), options));
    }

    private static IReadOnlyList<(string Branch, bool InsideWall)> WriteColumns(string path, PlanGeometrySet made)
    {
        Console.WriteLine($"{Path.GetFileName(path)}: {made.Columns.Count} columns, {made.Walls.Count} wall panels (sheet coordinates and drawing units)");
        var rows = new List<(string Branch, bool InsideWall)>();
        foreach (var column in made.Columns.OrderBy(c => c.Layer, StringComparer.Ordinal)
                     .ThenBy(c => c.Center.X).ThenBy(c => c.Center.Y).ThenBy(c => c.Width).ThenBy(c => c.Depth))
        {
            var origin = column.Origin;
            var walls = made.Walls.Where(w => LoopGeometry.WallContainsPoint(w, column.Center))
                .OrderBy(w => w.Length).ThenBy(w => w.Thickness).ToList();
            string containment = walls.Count == 0 ? "not inside a wall" : "inside wall " +
                string.Join("; ", walls.Select(w => $"length {w.Length:0.###} thickness {w.Thickness:0.###}"));
            Console.WriteLine($"  column ({column.Center.X:0.###},{column.Center.Y:0.###}) size {column.Width:0.###}x{column.Depth:0.###}" +
                $" layer {origin.Layer} loop {origin.LoopIndex} box {origin.LoopLength:0.###}x{origin.LoopThickness:0.###}" +
                $" branch {origin.Branch}{(column.FromBelow ? " below" : "")}  {containment}");
            rows.Add((origin.Branch, walls.Count > 0));
        }
        Console.WriteLine($"  unknown origins: {rows.Count(r => r.Branch == "unknown")}; loop -1 denotes a wall stub (box = axis length x thickness)");
        return rows;
    }
}
