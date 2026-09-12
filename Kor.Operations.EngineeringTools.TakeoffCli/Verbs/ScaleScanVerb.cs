// The takeoff verb `scale-scan`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Sheet-scale probe: the title-block SCALE each page states (SheetScaleReader) and the mpp it converts
// to — proves what the takeoff will measure at, page by page, before a run. Usage:
//   takeoff scale-scan <pdf> [first] [last]
internal static class ScaleScanVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("scale-scan", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        int scFirst = args.Length >= 3 && int.TryParse(args[2], out var sf) ? sf : 1;
        int scLast = args.Length >= 4 && int.TryParse(args[3], out var sl) ? sl : int.MaxValue;
        var scDig = DrawingDigestBuilder.Build(args[1], scFirst, scLast == int.MaxValue ? null : scLast);
        int stated = 0;
        foreach (var pg in scDig.Pages)
        {
            var spc = VectorPageReader.ReadPage(args[1], pg.Page);
            string? sn = SheetScaleReader.FromPage(spc);
            if (sn is not null) stated++;
            Console.WriteLine($"  p{pg.Page,-3} {pg.Title?.Display ?? "untitled",-18} scale: " +
                (sn is null ? "— (none stated / unparseable → fallback 1/8\"=1'-0\", flagged)"
                            : $"{sn.Trim()}  (mpp@110dpi {PlanGeometry.MetresPerPixel(sn, 110):0.000000})"));
        }
        Console.WriteLine($"{stated}/{scDig.Pages.Count} page(s) state a machine-readable scale.");
        return 0;
    }
}
