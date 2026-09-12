// The takeoff verb `vector-words`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Title-block probe: dump words by FONT SIZE (height) and normalized position, so we can see where the
// sheet title actually lives (corner? largest font?) vs stray cross-references. Usage:
//   takeoff vector-words <pdf> <page> [needle]
internal static class VectorWordsVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("vector-words", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff vector-words <pdf> <page> [needle]"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!int.TryParse(args[2], out int wPage) || wPage < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

        var pc = VectorPageReader.ReadPage(args[1], wPage);
        double W = pc.WidthPts, H = pc.HeightPts;
        // Normalized position: fx = fraction across (0=left,1=right), fy = fraction up from BOTTOM (PDF y).
        string Pos(VectorPageReader.TextToken t) => $"fx={t.Cx / W:F2} fy={t.Cy / H:F2}";

        Console.WriteLine($"Page {wPage}: {W:F0}x{H:F0} pts, {pc.Words.Count} words");
        if (args.Length >= 4)
        {
            string needle = args[3];
            var hits = pc.Words.Where(t => t.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                               .OrderByDescending(t => t.Height).ToList();
            Console.WriteLine($"  \"{needle}\" matches by font height ({hits.Count}):");
            foreach (var t in hits.Take(40))
                Console.WriteLine($"    h={t.Height,5:F1}  {Pos(t)}  \"{t.Text}\"");
        }
        else
        {
            Console.WriteLine("  Top 30 words by font height (h=pts):");
            foreach (var t in pc.Words.OrderByDescending(t => t.Height).Take(30))
                Console.WriteLine($"    h={t.Height,5:F1}  {Pos(t)}  \"{t.Text}\"");
        }
        return 0;
    }
}
