namespace Kor.Operations.EngineeringTools.Dxf;

internal static class DxfFloodFillPlateDetector
{
    /// <summary>
    /// The single biggest floor the drawn linework encloses. Unchanged behaviour: every existing
    /// caller wants one plate and wants the largest.
    /// </summary>
    public static bool TryRecover(
        IReadOnlyList<DxfSegment> slabSegments,
        PlanClassificationOptions options,
        out PlanLoop? plate,
        out string note)
    {
        var all = RecoverAll(slabSegments, options);
        plate = all.Count > 0 ? all[0] : null;
        note = plate is null
            ? string.Empty
            : $"Slab edges did not close as vectors, so one floor plate was recovered by flood-filling "
              + $"the drawn slab-edge linework — {plate.Area / 144:N0} sq ft in {plate.Points.Count} "
              + "corners. Treat as recovered geometry.";
        return plate is not null;
    }

    /// <summary>
    /// The floor a storey's wall panels enclose, at the walls' OUTER face. Andrea Neuviale,
    /// 25 Aug 2026, on the plate a perimeter wall stands in for: "it should always follow the outer
    /// edge of the walls"; 07 Aug: "just one thickness per floor, general outline at first."
    /// The rings a Revit export draws (<c>PairConcentricWallRings</c>) already give this; a plan
    /// whose walls arrive as separate panels — every plan the PDF route emits, and any export
    /// that draws walls one by one — gave nothing, and the storey had no diaphragm at all.
    ///
    /// The panels are painted solid on a raster, the paint is closed across gaps up to a doorway
    /// (<see cref="PlanClassificationOptions.MaxOpeningSpan"/>, the banked opening span: a wall
    /// stops at a doorway and the floor does not), the outside is flooded from the raster's edge
    /// and then pulled back by the same distance so the closing adds no width, and what is left is
    /// the walls and everything they enclose. Its boundary is the outer face. A ring that does not
    /// close leaks, the outside floods everything, and nothing is returned — the storey keeps
    /// having no plate rather than being given the sheet.
    /// </summary>
    public static PlanLoop? EnclosedByWallPanels(
        IReadOnlyList<WallAxis> walls,
        PlanClassificationOptions options,
        out string note)
    {
        note = string.Empty;
        if (walls.Count < 3) return null;

        var corners = new List<DxfPoint[]>();
        foreach (var w in walls)
        {
            double dx = w.End.X - w.Start.X, dy = w.End.Y - w.Start.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= 0) continue;
            double nx = -dy / len * w.Thickness / 2, ny = dx / len * w.Thickness / 2;
            corners.Add(new[]
            {
                new DxfPoint(w.Start.X + nx, w.Start.Y + ny), new DxfPoint(w.End.X + nx, w.End.Y + ny),
                new DxfPoint(w.End.X - nx, w.End.Y - ny), new DxfPoint(w.Start.X - nx, w.Start.Y - ny),
            });
        }
        if (corners.Count < 3) return null;

        double minX = corners.Min(c => c.Min(p => p.X)), minY = corners.Min(c => c.Min(p => p.Y));
        double maxX = corners.Max(c => c.Max(p => p.X)), maxY = corners.Max(c => c.Max(p => p.Y));
        double spanX = maxX - minX, spanY = maxY - minY;
        if (spanX <= 0 || spanY <= 0 || spanX * spanY < options.MinPlateArea) return null;

        const int maxEdgePixels = 1800;
        double bridge = Math.Max(options.MaxOpeningSpan, options.FloodFillBridge);
        double pixelSize = Math.Max(options.MinPanelOverlap / 2.0, Math.Max(spanX, spanY) / (maxEdgePixels - 2.0));
        if (pixelSize <= 0 || double.IsNaN(pixelSize) || double.IsInfinity(pixelSize)) return null;
        // the margin is the closing radius plus a border, so the outside can always be reached
        int radius = Math.Max(1, (int)Math.Ceiling(bridge / (2.0 * pixelSize)));
        int margin = radius + 4;
        int width = (int)Math.Ceiling(spanX / pixelSize) + margin * 2 + 1;
        int height = (int)Math.Ceiling(spanY / pixelSize) + margin * 2 + 1;
        if ((long)width * height > 6_000_000) return null;

        // 1. the panels, painted solid
        var solid = new bool[width * height];
        foreach (var c in corners)
        {
            int x0 = Math.Clamp((int)Math.Floor((c.Min(p => p.X) - minX) / pixelSize) + margin, 0, width - 1);
            int x1 = Math.Clamp((int)Math.Ceiling((c.Max(p => p.X) - minX) / pixelSize) + margin, 0, width - 1);
            int y0 = Math.Clamp((int)Math.Floor((c.Min(p => p.Y) - minY) / pixelSize) + margin, 0, height - 1);
            int y1 = Math.Clamp((int)Math.Ceiling((c.Max(p => p.Y) - minY) / pixelSize) + margin, 0, height - 1);
            var poly = c.ToList();
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var centre = new DxfPoint(minX + (x - margin) * pixelSize, minY + (y - margin) * pixelSize);
                if (LoopGeometry.PointInPolygon(centre, poly) || OnEdge(centre, poly, pixelSize * 0.75)) solid[y * width + x] = true;
            }
        }

        // 2. closed across doorways: dilate, flood the outside, erode the outside back
        var dilated = new bool[width * height];
        for (int i = 0; i < solid.Length; i++)
        {
            if (!solid[i]) continue;
            int x = i % width, y = i / width;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px >= 0 && py >= 0 && px < width && py < height) dilated[py * width + px] = true;
            }
        }
        // The closing is dilate-then-erode on the paint, which seen from the outside is: the
        // outside of the dilated paint, grown back by the same radius. A cell is outside when any
        // cell within the radius reached the raster's edge without crossing the dilated paint.
        var outsideDilated = FloodExterior(dilated, width, height);
        var outside = new bool[width * height];
        for (int i = 0; i < outside.Length; i++)
        {
            if (!outsideDilated[i]) continue;
            int x = i % width, y = i / width;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px >= 0 && py >= 0 && px < width && py < height) outside[py * width + px] = true;
            }
        }

        // 3. what is not outside is the walls and what they enclose; the largest piece is the floor
        var component = SolidComponents(solid, outside, width, height).FirstOrDefault();
        if (component is null || component.Count == 0) return null;
        var loops = BoundaryLoops(component, width, height);
        var pixelLoop = loops.OrderByDescending(AbsArea).FirstOrDefault();
        if (pixelLoop is null || pixelLoop.Count < 4) return null;

        var points = Simplify(pixelLoop
            .Select(p => new DxfPoint(minX + (p.X - margin) * pixelSize, minY + (p.Y - margin) * pixelSize))
            .ToList(), pixelSize * 1.5);
        if (points.Count < 3) return null;
        double straightenAt = Math.Max(options.RecoveredOutlineTolerance, pixelSize * 1.5);
        var straightened = LoopGeometry.Straighten(points, straightenAt);
        if (straightened.Count >= 3)
        {
            double before = Math.Abs(new PlanLoop("t", points, false).SignedArea);
            double after = Math.Abs(new PlanLoop("t", straightened, false).SignedArea);
            if (before > 0 && Math.Abs(after - before) / before <= 0.01) points = straightened;
        }

        // THE RASTER'S EDGE IS NOT THE WALL'S. A cell is painted when its centre lies within three
        // quarters of a cell of a panel, so the traced boundary runs up to a cell outside the outer
        // face — on a parkade about half a per cent of the area, which is the whole of the 0.4%
        // and 0.7% over Revit the first measurement read as agreement (Codex 31, F9). Each edge of
        // the loop is moved onto the panel edge it runs along, where one lies within a cell and a
        // half, and the corners are where the moved edges meet: the boundary IS the outer face.
        points = SnapToPanelEdges(points, corners, pixelSize * 1.5);

        var plate = new PlanLoop("walls' outer edge", points, closedExactly: false);
        if (plate.Area < options.MinPlateArea) return null;
        // the walls did not close and the outside flooded through: the paint left is the walls alone
        double painted = component.Count * pixelSize * pixelSize;
        double wallArea = walls.Sum(w => w.Length * w.Thickness);
        if (painted < 2 * wallArea) return null;

        note = $"No slab edge on this drawing would close, so the floor is taken from the OUTER face of "
             + $"the walls it stands on — {plate.Area / 144:N0} sq ft, one outline, one thickness, closed "
             + $"across gaps up to {bridge:0} in (a doorway). It is an approximation offered because a "
             + "storey with no plate has no diaphragm at all.";
        return plate;

        static bool OnEdge(DxfPoint p, List<DxfPoint> poly, double within)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double vx = b.X - a.X, vy = b.Y - a.Y, l2 = vx * vx + vy * vy;
                double t = l2 <= 0 ? 0 : Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / l2, 0, 1);
                double ex = a.X + vx * t - p.X, ey = a.Y + vy * t - p.Y;
                if (ex * ex + ey * ey <= within * within) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The loop with each edge moved onto the panel edge it runs along — parallel within a degree,
    /// within <paramref name="within"/>, overlapping it along its length — and each corner where
    /// its two moved edges meet. An edge running along no panel edge stays where the raster put
    /// it. The loop is returned unchanged when the moved one differs in area by more than a tenth,
    /// which is no longer a snap.
    /// </summary>
    internal static List<DxfPoint> SnapToPanelEdges(List<DxfPoint> loop, IReadOnlyList<DxfPoint[]> panels, double within)
    {
        if (loop.Count < 3) return loop;
        var panelEdges = new List<(DxfPoint A, DxfPoint B)>();
        foreach (var c in panels)
            for (int i = 0; i < c.Length; i++) panelEdges.Add((c[i], c[(i + 1) % c.Length]));

        // each loop edge as a line: a point on it and a unit direction — the panel's own line
        // where the edge runs along one. An edge along no panel, a few cells long, between two
        // that are, is a step of the raster at a corner and not an edge of the floor: dropped,
        // so the corner is where the two real edges meet.
        List<(DxfPoint P, double Ux, double Uy, bool Snapped)> lines;
        for (;;)
        {
            lines = new List<(DxfPoint P, double Ux, double Uy, bool Snapped)>(loop.Count);
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i]; var b = loop[(i + 1) % loop.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0) { lines.Add((a, 1, 0, false)); continue; }
                double ux = dx / len, uy = dy / len;
                (DxfPoint P, double Ux, double Uy, bool Snapped) line = (a, ux, uy, false);
                double bestOverlap = 0;
                foreach (var (p, q) in panelEdges)
                {
                    double ex = q.X - p.X, ey = q.Y - p.Y, el = Math.Sqrt(ex * ex + ey * ey);
                    if (el <= 0 || Math.Abs(ux * ey - uy * ex) / el > 0.0175) continue;          // not parallel within a degree
                    double off0 = (p.X - a.X) * -uy + (p.Y - a.Y) * ux, off1 = (q.X - a.X) * -uy + (q.Y - a.Y) * ux;
                    if (Math.Abs(off0) > within || Math.Abs(off1) > within) continue;              // not the edge this one runs along
                    double t0 = (p.X - a.X) * ux + (p.Y - a.Y) * uy, t1 = (q.X - a.X) * ux + (q.Y - a.Y) * uy;
                    double overlap = Math.Min(Math.Max(t0, t1), len) - Math.Max(Math.Min(t0, t1), 0);
                    if (overlap <= bestOverlap) continue;
                    bestOverlap = overlap;
                    // the panel's own line — its point AND its direction, which the raster's chord
                    // has only to within a fraction of a degree, six inches over a hundred feet —
                    // run the loop's way
                    double sign = ux * ex + uy * ey >= 0 ? 1 : -1;
                    line = (p, sign * ex / el, sign * ey / el, true);
                }
                lines.Add(line);
            }

            int step = -1;
            for (int i = 0; i < loop.Count && loop.Count > 3; i++)
            {
                if (lines[i].Snapped || loop[i].DistanceTo(loop[(i + 1) % loop.Count]) > 4 * within) continue;
                if (lines[(i - 1 + loop.Count) % loop.Count].Snapped && lines[(i + 1) % loop.Count].Snapped) { step = i; break; }
            }
            if (step < 0) break;
            loop = loop.Where((_, k) => k != (step + 1) % loop.Count).ToList();
        }

        var snapped = new List<DxfPoint>(loop.Count);
        for (int i = 0; i < loop.Count; i++)
        {
            var prev = lines[(i - 1 + loop.Count) % loop.Count]; var next = lines[i];
            double cross = prev.Ux * next.Uy - prev.Uy * next.Ux;
            if (Math.Abs(cross) < 1e-6)
            {
                // collinear neighbours: the corner projected onto the moved line
                double t = (loop[i].X - next.P.X) * next.Ux + (loop[i].Y - next.P.Y) * next.Uy;
                snapped.Add(new DxfPoint(next.P.X + next.Ux * t, next.P.Y + next.Uy * t));
                continue;
            }
            double s = ((next.P.X - prev.P.X) * next.Uy - (next.P.Y - prev.P.Y) * next.Ux) / cross;
            snapped.Add(new DxfPoint(prev.P.X + prev.Ux * s, prev.P.Y + prev.Uy * s));
        }

        double before = Math.Abs(new PlanLoop("t", loop, false).SignedArea);
        double after = Math.Abs(new PlanLoop("t", snapped, false).SignedArea);
        return before > 0 && Math.Abs(after - before) / before <= 0.10 ? snapped : loop;
    }

    /// <summary>
    /// EVERY floor the linework encloses, biggest first.
    ///
    /// The fill has always walked every solid region and then thrown all but the largest away. A
    /// sheet drawing three separate slabs therefore only ever yielded one, which is invisible while
    /// the other two close as vectors — 31168's mezzanine closes two cleanly and leaves the third,
    /// about 1,900 sq ft, with a 23 ft opening in its edge that no join tolerance should ever bridge.
    /// The engineer has said three times that the storey carries three.
    ///
    /// Used only where her count says a sheet is short, so nothing that works today changes.
    /// </summary>
    public static IReadOnlyList<PlanLoop> RecoverAll(
        IReadOnlyList<DxfSegment> slabSegments,
        PlanClassificationOptions options)
    {
        var plates = new List<PlanLoop>();

        if (slabSegments.Count < 12) return plates;

        double minX = slabSegments.Min(s => Math.Min(s.Start.X, s.End.X));
        double minY = slabSegments.Min(s => Math.Min(s.Start.Y, s.End.Y));
        double maxX = slabSegments.Max(s => Math.Max(s.Start.X, s.End.X));
        double maxY = slabSegments.Max(s => Math.Max(s.Start.Y, s.End.Y));
        double spanX = maxX - minX;
        double spanY = maxY - minY;
        if (spanX <= 0 || spanY <= 0 || spanX * spanY < options.MinPlateArea) return plates;

        const int margin = 8;
        const int maxEdgePixels = 1800;
        double pixelSize = Math.Max(options.MinPanelOverlap / 2.0, Math.Max(spanX, spanY) / (maxEdgePixels - margin * 2.0));
        if (pixelSize <= 0 || double.IsNaN(pixelSize) || double.IsInfinity(pixelSize)) return plates;

        int width = Math.Max(3, (int)Math.Ceiling(spanX / pixelSize) + margin * 2 + 1);
        int height = Math.Max(3, (int)Math.Ceiling(spanY / pixelSize) + margin * 2 + 1);
        if ((long)width * height > 4_000_000) return plates;

        var dark = new bool[width * height];
        // The bridge is the size of the INTERRUPTIONS in a slab edge, not the size of a dash.
        double bridge = options.FloodFillBridge > 0 ? options.FloodFillBridge : options.DashJoinGap;
        int strokeRadius = Math.Max(1, (int)Math.Ceiling(bridge / (2.0 * pixelSize)));

        (int X, int Y) Map(DxfPoint p)
        {
            int x = (int)Math.Round((p.X - minX) / pixelSize) + margin;
            int y = (int)Math.Round((p.Y - minY) / pixelSize) + margin;
            return (Math.Clamp(x, 0, width - 1), Math.Clamp(y, 0, height - 1));
        }

        foreach (var segment in slabSegments)
        {
            var a = Map(segment.Start);
            var b = Map(segment.End);
            int steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) + 1;
            for (int i = 0; i <= steps; i++)
            {
                double t = steps == 0 ? 0 : (double)i / steps;
                int x = (int)Math.Round(a.X + (b.X - a.X) * t);
                int y = (int)Math.Round(a.Y + (b.Y - a.Y) * t);
                Plot(x, y);
            }
        }

        var exterior = FloodExterior(dark, width, height);

        // Every enclosed region, not only the biggest. Each is put through exactly the same
        // straightening and the same two gates the single-plate path has always applied.
        foreach (var component in SolidComponents(dark, exterior, width, height))
        {
            var loops = BoundaryLoops(component, width, height);
            var pixelLoop = loops.OrderByDescending(AbsArea).FirstOrDefault();
            if (pixelLoop is null || pixelLoop.Count < 4) continue;

            var points = Simplify(pixelLoop
                .Select(p => new DxfPoint(
                    minX + (p.X - margin) * pixelSize,
                    minY + (p.Y - margin) * pixelSize))
                .ToList(), pixelSize * 1.5);

            if (points.Count < 3) continue;

            // A TRACE IS NOT AN OUTLINE UNTIL IT IS STRAIGHTENED.
            //
            // The collinear pass above removes a vertex only where it sits on the line between its
            // two neighbours, and on a raster staircase none of them does. 31168's LEVEL 2 shipped
            // as 1,942 vertices and C-LEVEL 3 as 1,084, against perhaps forty in the drawing. ETABS
            // is handed the staircase and meshes it, and no count in the report can see that.
            //
            // AND NEVER BELOW THE CELL IT WAS DRAWN ON. A trace cannot carry detail finer than the
            // raster that made it, so straightening under one cell can only preserve the raster's
            // own staircase -- which is what happened. The cell here is MinPanelOverlap / 2 = 6 in
            // and the rule is 3 in, exactly half, so every 6 in step survived: 31168's LEVEL 2
            // shipped with 67 segments of exactly 6.0 in alternating V, H, V, H along a diagonal
            // edge. Andrea Neuviale sent a picture of it on 31 August -- a flight of stairs where
            // the drawing has one straight line.
            //
            // Straightened at the rule or the cell, whichever is coarser, then CHECKED: an outline
            // that loses real area under it is not simplified, it is different, so the straightening
            // is kept only where area holds.
            double straightenAt = Math.Max(options.RecoveredOutlineTolerance, pixelSize * 1.5);
            var straightened = LoopGeometry.Straighten(points, straightenAt);
            if (straightened.Count >= 3)
            {
                double before = Math.Abs(new PlanLoop("t", points, false).SignedArea);
                double after = Math.Abs(new PlanLoop("t", straightened, false).SignedArea);
                if (before > 0 && Math.Abs(after - before) / before <= 0.01) points = straightened;
            }

            var recovered = new PlanLoop("slab-edge flood fill", points, closedExactly: false);
            if (recovered.Area < options.MinPlateArea) continue;
            if (BoundingFillRatio(recovered.Points, recovered.Area) < 0.80) continue;

            plates.Add(recovered);
        }

        plates.Sort((a, b) => b.Area.CompareTo(a.Area));
        return plates;

        void Plot(int x, int y)
        {
            for (int dy = -strokeRadius; dy <= strokeRadius; dy++)
            for (int dx = -strokeRadius; dx <= strokeRadius; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px < 0 || py < 0 || px >= width || py >= height) continue;
                dark[py * width + px] = true;
            }
        }
    }

    private static double BoundingFillRatio(IReadOnlyList<DxfPoint> points, double area)
    {
        double minX = points.Min(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxX = points.Max(p => p.X);
        double maxY = points.Max(p => p.Y);
        double boxArea = (maxX - minX) * (maxY - minY);
        return boxArea <= 0 ? 0 : area / boxArea;
    }

    private static bool[] FloodExterior(bool[] dark, int width, int height)
    {
        var exterior = new bool[dark.Length];
        var q = new Queue<int>();

        void Seed(int x, int y)
        {
            int i = y * width + x;
            if (dark[i] || exterior[i]) return;
            exterior[i] = true;
            q.Enqueue(i);
        }

        for (int x = 0; x < width; x++) { Seed(x, 0); Seed(x, height - 1); }
        for (int y = 0; y < height; y++) { Seed(0, y); Seed(width - 1, y); }

        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int x = i % width;
            int y = i / width;
            Visit(x - 1, y);
            Visit(x + 1, y);
            Visit(x, y - 1);
            Visit(x, y + 1);
        }

        return exterior;

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int i = y * width + x;
            if (dark[i] || exterior[i]) return;
            exterior[i] = true;
            q.Enqueue(i);
        }
    }

    /// <summary>Every enclosed solid region, largest first. This walk always found them all; it
    /// used to keep only the biggest.</summary>
    private static List<HashSet<int>> SolidComponents(bool[] dark, bool[] exterior, int width, int height)
    {
        var visited = new bool[dark.Length];
        var found = new List<HashSet<int>>();
        var q = new Queue<int>();

        for (int start = 0; start < dark.Length; start++)
        {
            if (exterior[start] || visited[start]) continue;

            var current = new HashSet<int>();
            visited[start] = true;
            q.Enqueue(start);

            while (q.Count > 0)
            {
                int i = q.Dequeue();
                current.Add(i);
                int x = i % width;
                int y = i / width;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            found.Add(current);
        }

        found.Sort((a, b) => b.Count.CompareTo(a.Count));
        return found;

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int i = y * width + x;
            if (exterior[i] || visited[i]) return;
            visited[i] = true;
            q.Enqueue(i);
        }
    }

    private readonly record struct PixelPoint(int X, int Y);

    private static List<List<PixelPoint>> BoundaryLoops(HashSet<int> component, int width, int height)
    {
        var next = new Dictionary<PixelPoint, List<PixelPoint>>();

        void Add(PixelPoint a, PixelPoint b)
        {
            if (!next.TryGetValue(a, out var list)) next[a] = list = new List<PixelPoint>();
            list.Add(b);
        }

        foreach (int i in component)
        {
            int x = i % width;
            int y = i / width;
            if (!component.Contains(i - width))
                Add(new PixelPoint(x, y), new PixelPoint(x + 1, y));
            if (x == width - 1 || !component.Contains(i + 1))
                Add(new PixelPoint(x + 1, y), new PixelPoint(x + 1, y + 1));
            if (y == height - 1 || !component.Contains(i + width))
                Add(new PixelPoint(x + 1, y + 1), new PixelPoint(x, y + 1));
            if (x == 0 || !component.Contains(i - 1))
                Add(new PixelPoint(x, y + 1), new PixelPoint(x, y));
        }

        var loops = new List<List<PixelPoint>>();
        while (next.Count > 0)
        {
            var start = next.Keys.First();
            var loop = new List<PixelPoint> { start };
            var current = start;

            while (next.TryGetValue(current, out var exits) && exits.Count > 0)
            {
                var to = exits[^1];
                exits.RemoveAt(exits.Count - 1);
                if (exits.Count == 0) next.Remove(current);

                current = to;
                if (current.Equals(start)) break;
                loop.Add(current);
            }

            if (loop.Count > 2) loops.Add(loop);
        }

        return loops;
    }

    private static double AbsArea(IReadOnlyList<PixelPoint> points)
    {
        double sum = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return Math.Abs(sum) / 2.0;
    }

    private static List<DxfPoint> Simplify(List<DxfPoint> points, double tolerance)
    {
        var collinear = new List<DxfPoint>();
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[(i - 1 + points.Count) % points.Count];
            var b = points[i];
            var c = points[(i + 1) % points.Count];
            double cross = Math.Abs((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X));
            double len = b.DistanceTo(a) + c.DistanceTo(b);
            if (len > 0 && cross / len <= tolerance) continue;
            collinear.Add(b);
        }

        return collinear.Count >= 3 ? collinear : points;
    }
}
