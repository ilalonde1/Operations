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
