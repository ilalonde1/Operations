// The takeoff verb `pdf-readable`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// PRE-CHECK — can the vector takeoff even read this set? Cheap (text layer only, no raster/AI). Drop a bid
// PDF here first: READABLE means the takeoff will run; BLIND means it's a scanned/flattened set the tool
// cannot read (and will refuse rather than bluff). Usage: takeoff pdf-readable <pdf> [first] [last]
internal static class PdfReadableVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("pdf-readable", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: takeoff pdf-readable <pdf> [first] [last]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        int? rFirst = args.Length >= 3 && int.TryParse(args[2], out var rf) ? rf : null;
        int? rLast  = args.Length >= 4 && int.TryParse(args[3], out var rl) ? rl : null;

        var verdict = SlabTakeoffEngine.AssessReadability(args[1], rFirst, rLast);
        Console.WriteLine($"\n{(verdict.Readable ? "READABLE" : "BLIND")} — {Path.GetFileName(args[1])}");
        Console.WriteLine($"  pages: {verdict.PagesInRange}   text pages: {verdict.TextPages}   image-only pages: {verdict.ImageOnlyPages}   median words/text page: {verdict.MedianWordsPerTextPage}");
        Console.WriteLine($"  {verdict.Reason}");
        return verdict.Readable ? 0 : 3;
    }
}
