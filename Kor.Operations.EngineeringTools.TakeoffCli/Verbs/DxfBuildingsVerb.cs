// The takeoff verb `dxf-buildings`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
internal static class DxfBuildingsVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("dxf-buildings", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff dxf-buildings <dxfFolder> <reference.e2k>"); return 1; }
        if (!Directory.Exists(args[1])) { Console.Error.WriteLine($"Not found '{args[1]}'."); return 2; }
        if (!File.Exists(args[2])) { Console.Error.WriteLine($"Not found '{args[2]}'."); return 2; }

        var refDoc = E2kDocument.Load(args[2]);
        var allStoreys = refDoc.ReadStories().Select(s => s.Name).ToList();
        var classifyWith = new PlanClassificationOptions();

        var reach = new Dictionary<string, (double MinX, double MinY, double MaxX, double MaxY)>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(args[1], "*.dxf", SearchOption.TopDirectoryOnly))
        {
            var sheet = PlanSheetNaming.Parse(file);
            var on = PlanSheetNaming.MatchStories(sheet, allStoreys);
            if (on.Count == 0) continue;

            var found = StructuralPlanClassifier.Classify(DxfPlanReader.ReadSegments(file), classifyWith);
            var pts = found.Walls.SelectMany(w => new[] { w.Start, w.End })
                .Concat(found.Columns.Select(c => c.Center))
                .ToList();
            if (pts.Count == 0) continue;

            var box = (pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
            foreach (string storey in on)
                reach[storey] = reach.TryGetValue(storey, out var had)
                    ? (Math.Min(had.MinX, box.Item1), Math.Min(had.MinY, box.Item2),
                       Math.Max(had.MaxX, box.Item3), Math.Max(had.MaxY, box.Item4))
                    : box;
        }

        var tags = allStoreys.Select(E2kDocument.BuildingTagOf).Where(t => t.Length > 0).Distinct().OrderBy(t => t).ToList();
        if (tags.Count == 0) { Console.WriteLine("ONE\t" + string.Join(",", allStoreys)); return 0; }

        var footprint = new Dictionary<string, (double MinX, double MinY, double MaxX, double MaxY)>(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in tags)
            foreach (string storey in allStoreys.Where(s => E2kDocument.BuildingTagOf(s).Equals(tag, StringComparison.OrdinalIgnoreCase)))
                if (reach.TryGetValue(storey, out var box))
                    footprint[tag] = footprint.TryGetValue(tag, out var had)
                        ? (Math.Min(had.MinX, box.MinX), Math.Min(had.MinY, box.MinY),
                           Math.Max(had.MaxX, box.MaxX), Math.Max(had.MaxY, box.MaxY))
                        : box;

        static double Overlap((double MinX, double MinY, double MaxX, double MaxY) a,
                              (double MinX, double MinY, double MaxX, double MaxY) b)
        {
            double w = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
            double h = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
            double own = (a.MaxX - a.MinX) * (a.MaxY - a.MinY);
            return w > 0 && h > 0 && own > 0 ? w * h / own : 0;
        }

        foreach (string tag in tags)
        {
            var mine = new List<string>();
            foreach (string storey in allStoreys)
            {
                string owner = E2kDocument.BuildingTagOf(storey);
                if (owner.Length > 0) { if (owner.Equals(tag, StringComparison.OrdinalIgnoreCase)) mine.Add(storey); continue; }
                if (!reach.TryGetValue(storey, out var box)) { mine.Add(storey); continue; }
                if (!footprint.TryGetValue(tag, out var mineBox)) continue;

                bool here = Overlap(box, mineBox) >= 0.5;
                bool shared = tags.All(t => footprint.TryGetValue(t, out var f) && Overlap(f, box) >= 0.5);
                if (here || shared) mine.Add(storey);
            }

            Console.WriteLine($"{tag}\t{string.Join(",", mine)}");
        }
        return 0;
    }
}
