// The takeoff verb `dedupe-probe`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// DIAGNOSTIC: takeoff dedupe-probe <pdf> <page> <needle> — PdfPig words matching needle with EXACT
// coordinates, before and after PdfWordDedupe, to verify double-draw (fake-bold) collapsing.
internal static class DedupeProbeVerb
{
    public static bool Matches(string[] args) => args.Length >= 4 && args[0].Equals("dedupe-probe", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        using var dpDoc = UglyToad.PdfPig.PdfDocument.Open(args[1]);
        var dpPage = dpDoc.GetPage(int.Parse(args[2]));
        var raw = dpPage.GetWords().ToList();
        var kept = Kor.Operations.EngineeringTools.RebarChange.PdfWordDedupe.Filter(raw);
        Console.WriteLine($"page {args[2]}: raw {raw.Count} words -> deduped {kept.Count}");
        foreach (var (label, list) in new[] { ("RAW", raw), ("KEPT", kept) })
        {
            var hits = list.Where(w => w.Text.Contains(args[3], StringComparison.OrdinalIgnoreCase))
                           .OrderBy(w => w.BoundingBox.Left).ThenBy(w => w.BoundingBox.Bottom).ToList();
            Console.WriteLine($"  {label}: {hits.Count} '{args[3]}' hit(s)");
            foreach (var w in hits.Take(30))
                Console.WriteLine($"    L={w.BoundingBox.Left:F2} B={w.BoundingBox.Bottom:F2} \"{w.Text}\"");
        }
        return 0;
    }
}
