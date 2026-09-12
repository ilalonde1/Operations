// The takeoff verb `dxf-render`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// RENDER a plan's structural layers to an image, so what the drawing contains can be seen
// rather than inferred. Walls red, columns blue, slab edges grey.
// Usage: takeoff dxf-render <plan.dxf> <out.png> [--size 1800] [--layers A,B] [--window x0,y0,x1,y1 (feet)]
internal static class DxfRenderVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("dxf-render", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff dxf-render <plan.dxf> <out.png> [--size 1800] [--layers A,B] [--window x0,y0,x1,y1 (feet)]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"Not found '{args[1]}'."); return 2; }

        int size = 1800;
        for (int i = 3; i < args.Length - 1; i++)
            if (args[i].Equals("--size", StringComparison.OrdinalIgnoreCase)) int.TryParse(args[i + 1], out size);

        var renderOptions = new PlanClassificationOptions();

        // --layers narrows the drawing to the layers named, so one question can be looked at on its
        // own. A slab boundary is impossible to judge with every wall and column drawn over it.
        var onlyLayers = new List<string>();
        for (int i = 3; i < args.Length - 1; i++)
            if (args[i].Equals("--layers", StringComparison.OrdinalIgnoreCase))
                onlyLayers.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));

        var renderSegments = DxfPlanReader.ReadSegments(args[1])
            .Where(s => onlyLayers.Count > 0
                ? onlyLayers.Any(l => s.Layer.Contains(l, StringComparison.OrdinalIgnoreCase))
                : PlanClassificationOptions.Matches(s.Layer, renderOptions.WallLayerPatterns)
                     || PlanClassificationOptions.Matches(s.Layer, renderOptions.ColumnLayerPatterns)
                     || PlanClassificationOptions.Matches(s.Layer, renderOptions.SlabLayerPatterns))
            .ToList();

        if (renderSegments.Count == 0) { Console.Error.WriteLine("No structural layers in this drawing."); return 3; }

        // --window x0,y0,x1,y1 IN FEET narrows the render to one part of the sheet. A storey drawn at
        // 1,800 px across puts a 40 ft slab in 300 of them, and the question is usually about one
        // region: whether a slab edge really is interrupted there, or what the fill traced.
        for (int i = 3; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--window", StringComparison.OrdinalIgnoreCase)) continue;
            var box = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.TryParse(v.Trim(), out double f) ? f * 12.0 : double.NaN)
                .ToArray();
            if (box.Length != 4 || box.Any(double.IsNaN))
            {
                Console.Error.WriteLine("--window wants x0,y0,x1,y1 in feet."); return 1;
            }

            renderSegments = renderSegments
                .Where(s => Math.Max(s.Start.X, s.End.X) >= box[0] && Math.Min(s.Start.X, s.End.X) <= box[2]
                         && Math.Max(s.Start.Y, s.End.Y) >= box[1] && Math.Min(s.Start.Y, s.End.Y) <= box[3])
                .ToList();
            if (renderSegments.Count == 0) { Console.Error.WriteLine("Nothing drawn in that window."); return 3; }
        }

        double minX = renderSegments.Min(s => Math.Min(s.Start.X, s.End.X));
        double maxX = renderSegments.Max(s => Math.Max(s.Start.X, s.End.X));
        double minY = renderSegments.Min(s => Math.Min(s.Start.Y, s.End.Y));
        double maxY = renderSegments.Max(s => Math.Max(s.Start.Y, s.End.Y));

        double scale = (size - 40) / Math.Max(maxX - minX, maxY - minY);
        int width = (int)((maxX - minX) * scale) + 40;
        int height = (int)((maxY - minY) * scale) + 40;

        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255));
        (double X, double Y) Map(DxfPoint p) => ((p.X - minX) * scale + 20, height - 20 - (p.Y - minY) * scale);

        void Plot(int x, int y, Rgba32 colour, int weight)
        {
            for (int dx = -weight; dx <= weight; dx++)
                for (int dy = -weight; dy <= weight; dy++)
                {
                    int px = x + dx, py = y + dy;
                    if (px >= 0 && py >= 0 && px < width && py < height) image[px, py] = colour;
                }
        }

        foreach (var seg in renderSegments)
        {
            bool isWallLayer = PlanClassificationOptions.Matches(seg.Layer, renderOptions.WallLayerPatterns);
            bool isColumnLayer = PlanClassificationOptions.Matches(seg.Layer, renderOptions.ColumnLayerPatterns);
            var colour = isWallLayer ? new Rgba32(200, 30, 40)
                       : isColumnLayer ? new Rgba32(30, 70, 200)
                       : new Rgba32(180, 180, 180);
            int weight = isWallLayer || isColumnLayer ? 1 : 0;

            var (x0, y0) = Map(seg.Start);
            var (x1, y1) = Map(seg.End);
            int steps = (int)Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1;
            for (int s = 0; s <= steps; s++)
            {
                double f = steps == 0 ? 0 : (double)s / steps;
                Plot((int)Math.Round(x0 + (x1 - x0) * f), (int)Math.Round(y0 + (y1 - y0) * f), colour, weight);
            }
        }

        // --text draws every TEXT tag as the box it will occupy: its insertion point, its height, and
        // roughly its width. Not the glyphs — the EXTENT, which is what tells you whether the labels
        // are the right size and whether they collide.
        //
        // Twice now a DXF has gone out with unreadable text and been caught by a person opening it in
        // CAD: once every word was drawn at a flat 250 mm from its centre instead of its baseline, and
        // once every markup label took its height from the SHAPE it annotates, so "12" x 30"" was set
        // 796 mm tall on a 796 mm wall. Both were invisible to a renderer that draws only geometry, and
        // both were obvious the moment somebody looked.
        if (args.Any(a => a.Equals("--text", StringComparison.OrdinalIgnoreCase)))
        {
            var tags = DxfPlanReader.ReadPositionedTags(args[1]);
            int drawn = 0;
            foreach (var tag in tags)
            {
                double h = tag.Height > 0 ? tag.Height : 240.0;
                double w = Math.Max(1, tag.Text.Length) * h * 0.6;
                var corners = new[]
                {
                    (tag.Point.X, tag.Point.Y), (tag.Point.X + w, tag.Point.Y),
                    (tag.Point.X + w, tag.Point.Y + h), (tag.Point.X, tag.Point.Y + h),
                };
                for (int i = 0; i < 4; i++)
                {
                    var (ax, ay) = Map(new DxfPoint(corners[i].Item1, corners[i].Item2));
                    var (bx, by) = Map(new DxfPoint(corners[(i + 1) % 4].Item1, corners[(i + 1) % 4].Item2));
                    int steps = (int)Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)) + 1;
                    for (int s = 0; s <= steps; s++)
                    {
                        double f = steps == 0 ? 0 : (double)s / steps;
                        Plot((int)Math.Round(ax + (bx - ax) * f), (int)Math.Round(ay + (by - ay) * f),
                             new Rgba32(0, 150, 0), 0);
                    }
                }
                drawn++;
            }
            var heights = tags.Where(t => t.Height > 0).Select(t => t.Height).OrderBy(h => h).ToList();
            Console.WriteLine(heights.Count == 0
                ? $"  text extents drawn: {drawn} (no heights in the file; boxed at 240)"
                : $"  text extents drawn: {drawn}  height {heights[0]:N0}-{heights[^1]:N0} mm "
                  + $"(median {heights[heights.Count / 2]:N0})");
        }

        // Overlay what the classifier actually extracted, so anything in the drawing that the
        // model does not carry is visible as bare linework rather than having to be inferred.
        if (args.Any(a => a.Equals("--overlay", StringComparison.OrdinalIgnoreCase)))
        {
            var extracted = StructuralPlanClassifier.Classify(DxfPlanReader.ReadSegments(args[1]), renderOptions);

            foreach (var wall in extracted.Walls)
            {
                var (ax, ay) = Map(wall.Start);
                var (bx, by) = Map(wall.End);
                int steps = (int)Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)) + 1;
                for (int s = 0; s <= steps; s++)
                {
                    double f = steps == 0 ? 0 : (double)s / steps;
                    Plot((int)Math.Round(ax + (bx - ax) * f), (int)Math.Round(ay + (by - ay) * f), new Rgba32(0, 170, 60), 2);
                }
            }

            foreach (var column in extracted.Columns)
            {
                var (px, py) = Map(column.Center);
                Plot((int)Math.Round(px), (int)Math.Round(py), new Rgba32(255, 140, 0), 4);
            }

            Console.WriteLine($"overlay: {extracted.Walls.Count} wall axes (green), {extracted.Columns.Count} columns (orange), {extracted.Slabs.Count} slabs");
        }

        image.Save(args[2]);
        Console.WriteLine($"{args[2]}  ({width}x{height})  segments drawn: {renderSegments.Count}");
        foreach (var g in renderSegments.GroupBy(s => s.Layer).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Key,-24} {g.Count()}");
        return 0;
    }
}
