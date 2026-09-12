// The takeoff verb `vector-words`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Title-block probe: dump words by FONT SIZE (height) and normalized position, so we can see where the
// sheet title actually lives (corner? largest font?) vs stray cross-references. Usage:
//   takeoff vector-words <pdf> <page> [needle]
//   takeoff vector-words <pdf> <page> --band y0 y1 [x0 x1]
// --band (from docs/etabs-handoff/pdf_words_in_band.py, WP2 2026-09-11): GROUND TRUTH for a row of grid
// bubbles — every word the page places in a band (PDF points, y UP), with its position and how many other
// words share its spot within 3 pt. Two words on one spot is what a bubble drawn twice looks like (an
// architect's underlay under the engineer's grid), and GridBubbles wants one word inside a circle. Built
// when 31168's 2026-09-10 reissue read 9 of 28 grid axes on S2.04.1 and the 08-25 issue read 26 of 26.
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
        if (args.Length >= 6 && args[3].Equals("--band", StringComparison.OrdinalIgnoreCase))
        {
            double y0 = Band(args[4]), y1 = Band(args[5]);
            double x0 = args.Length > 6 ? Band(args[6]) : double.NegativeInfinity, x1 = args.Length > 7 ? Band(args[7]) : double.PositiveInfinity;
            var band = pc.Words.Where(t => t.Cy >= y0 && t.Cy <= y1 && t.Cx >= x0 && t.Cx <= x1).OrderBy(t => t.Cx).ThenBy(t => t.Cy).ToList();
            Console.WriteLine($"  {band.Count} words in y {y0:0}..{y1:0}" + (args.Length > 6 ? $", x {x0:0}..{x1:0}" : ""));
            foreach (var t in band)
            {
                int twins = band.Count(o => Math.Abs(o.Cx - t.Cx) <= 3 && Math.Abs(o.Cy - t.Cy) <= 3) - 1;
                Console.WriteLine($"    x {t.Cx,8:0.0}  y {t.Cy,7:0.0}  \"{t.Text}\"" + (twins > 0 ? $"   +{twins} on the same spot" : ""));
            }
            return 0;
        }
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

        static double Band(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
