// The takeoff verb `vector-dump`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
internal static class VectorDumpVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-dump", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-dump <pdf> <page>"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!int.TryParse(args[2], out int pageNo) || pageNo < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

        var pc = VectorPageReader.ReadPage(args[1], pageNo);
        int closed = pc.Paths.Count(p => p.IsClosed);
        Console.WriteLine($"Page {pc.PageNumber}: {pc.WidthPts:F0}x{pc.HeightPts:F0} pts");
        Console.WriteLine($"  Text:     {pc.Words.Count} words (exact, no OCR)");
        Console.WriteLine($"  Geometry: {pc.Paths.Count} subpaths ({closed} closed / {pc.Paths.Count - closed} open)");

        Console.WriteLine("  Largest closed regions (candidate slabs/zones), bbox in pts:");
        foreach (var g in pc.Paths.Where(p => p.IsClosed).OrderByDescending(p => p.DiagonalLen).Take(6))
            Console.WriteLine($"    {g.Width:F0}x{g.Height:F0}  ({g.Points.Count} pts){(g.IsFilled ? " filled" : "")}");

        // Optional 4th arg: a substring filter — print every matching token with its position.
        if (args.Length >= 4)
        {
            string needle = args[3];
            var hits = pc.Words.Where(t => t.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
            Console.WriteLine($"  Tokens containing \"{needle}\": {hits.Count}");
            foreach (var t in hits.Take(40))
                Console.WriteLine($"    \"{t.Text}\" @ {t.Cx:F0},{t.Cy:F0}");
        }
        else
        {
            Console.WriteLine("  Sample text tokens (text @ x,y):");
            foreach (var t in pc.Words.Take(12))
                Console.WriteLine($"    \"{t.Text}\" @ {t.Cx:F0},{t.Cy:F0}");
        }
        return 0;
    }
}
