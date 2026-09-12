// The takeoff verb `vector-digest`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Layer-1 digest: emit the per-page structured facts (lines, geometry regions, scale, wall bands) the
// synthesis reasons over. Usage: takeoff vector-digest <pdf> <out.json> [firstPage] [lastPage]
internal static class VectorDigestVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-digest", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-digest <pdf> <out.json> [firstPage] [lastPage]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        int? first = args.Length >= 4 && int.TryParse(args[3], out var f) ? f : null;
        int? last  = args.Length >= 5 && int.TryParse(args[4], out var l) ? l : null;

        var digest = DrawingDigestBuilder.Build(args[1], first, last);
        var json = JsonSerializer.Serialize(digest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(args[2], json);
        Console.WriteLine($"Digest: {digest.Pages.Count} page(s) of {digest.PageCount} -> {args[2]} ({json.Length:N0} chars)");
        foreach (var pg in digest.Pages)
            Console.WriteLine($"  p{pg.Page}: {pg.Lines.Count} lines, {pg.ClosedRegions.Count} regions, {pg.WallBands.Count} wall bands, scale '{pg.ScaleNote ?? "?"}'");
        return 0;
    }
}
