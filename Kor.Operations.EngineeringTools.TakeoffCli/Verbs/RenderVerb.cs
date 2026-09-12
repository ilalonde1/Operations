// The takeoff verb `render`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// DIAGNOSTIC: takeoff render <pdf> <pngDir> [dpi] [first] [last] — rasterize pages to p-NN.png for inspection.
internal static class RenderVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("render", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        double rdpi = args.Length >= 4 && double.TryParse(args[3], out var rd) ? rd : 110;
        int? rf = args.Length >= 5 && int.TryParse(args[4], out var ra) ? ra : null;
        int? rl = args.Length >= 6 && int.TryParse(args[5], out var rb) ? rb : null;
        Directory.CreateDirectory(args[2]);
        int rn = PlanPdfRenderer.RenderMissing(args[1], args[2], rdpi, rf, rl);
        Console.WriteLine($"rendered {rn} page(s) -> {args[2]} @ {rdpi:0} dpi");
        return 0;
    }
}
