// The takeoff verb `sched-tokens`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// DIAGNOSTIC: takeoff sched-tokens <pdf> <page> — dump every token whose text contains a schedule keyword,
// with font height + x-fraction, to tell a real schedule-sheet TITLE from a prose mention on a notes sheet.
internal static class SchedTokensVerb
{
    public static bool Matches(string[] args) => args.Length >= 3 && args[0].Equals("sched-tokens", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var pc = VectorPageReader.ReadPage(args[1], int.Parse(args[2]));
        double pw = pc.WidthPts;
        var hits = pc.Words
            .Where(t => t.Text.ToUpperInvariant() is var u && (u.Contains("SCHEDULE") || u.Contains("SCHED")
                        || u.Contains("COLUMN") || u.Contains("SHEAR") || u.Contains("WALL")))
            .OrderByDescending(t => t.Height).ToList();
        Console.WriteLine($"page {args[2]}: w={pw:N0}pt, {pc.Words.Count} tokens; schedule-keyword tokens:");
        foreach (var t in hits.Take(40))
            Console.WriteLine($"  h={t.Height,6:N1}  fx={t.Cx / pw,4:0.00}  \"{t.Text}\"");
        double maxH = pc.Words.Count > 0 ? pc.Words.Max(t => t.Height) : 0;
        Console.WriteLine($"  (largest token on page: h={maxH:N1})");
        return 0;
    }
}
