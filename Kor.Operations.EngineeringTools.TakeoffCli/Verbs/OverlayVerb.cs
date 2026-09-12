// The takeoff verb `overlay`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Visual-markup mode (faithful to the app's GenerateOverlay_Click): on-drawing red/green markup.
// Usage: takeoff overlay <before.pdf> <after.pdf> <out.pdf> [name] [beforeLabel] [afterLabel] [imperial]
internal static class OverlayVerb
{
    public static bool Matches(string[] args) => args.Length >= 4 && args[0].Equals("overlay", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        // Same honest front door as `rebar`: refuse a scanned/flattened side up front rather than emit a
        // falsely-reassuring "no changes" markup off a drawing the tool cannot read.
        foreach (var (label, path) in new[] { ("BEFORE", args[1]), ("AFTER", args[2]) })
        {
            var orv = PdfReadabilityAssessor.AssessPageTexts(PdfPageTextReader.ReadPages(path));
            if (!orv.Readable)
            {
                Console.Error.WriteLine($"CANNOT READ THE {label} SET ({Path.GetFileName(path)}) — {orv.Reason}");
                return 3;
            }
        }

        string oname = args.Length > 4 ? args[4] : string.Empty;
        string obl   = args.Length > 5 ? args[5] : "Before";
        string oal   = args.Length > 6 ? args[6] : "After";
        var ounit = (args.Length > 7 && args[7].Equals("imperial", StringComparison.OrdinalIgnoreCase))
            ? UnitSystem.Imperial : UnitSystem.Metric;
        byte[] obytes;
        try { obytes = RebarOverlayGenerator.Build(args[1], args[2], oname, obl, oal, ounit); }
        catch (InvalidOperationException ex) { Console.Error.WriteLine($"ABORT: {ex.Message}"); return 3; }
        File.WriteAllBytes(args[3], obytes);
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(obytes))
        {
            int nbm = doc.TryGetBookmarks(out var bms) ? bms.Roots.Count : 0;
            Console.WriteLine($"Markup pages: {doc.NumberOfPages}   bookmarks: {nbm}");
        }
        Console.WriteLine($"Wrote {args[3]}");
        return 0;
    }
}
