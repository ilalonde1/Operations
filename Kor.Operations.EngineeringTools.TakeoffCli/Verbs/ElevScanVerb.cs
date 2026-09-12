// The takeoff verb `elev-scan`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// DIAGNOSTIC: takeoff elev-scan <pdf> [first] [last] — find where the set states floor elevations / storey
// heights. Scans every page for elevation-pattern tokens (feet-inch "100'-0", metric "+45.000", "EL"/"T.O.")
// and for "FLOOR TO FLOOR"/"STOREY" notes, and on any page carrying a level ladder dumps each LEVEL row's full
// baseline so a level→elevation column (if present) is visible. Verifies the real source before any reader.
internal static class ElevScanVerb
{
    public static bool Matches(string[] args) => args.Length >= 2 && args[0].Equals("elev-scan", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        int ef = args.Length >= 3 && int.TryParse(args[2], out var a) ? a : 1;
        int el = args.Length >= 4 && int.TryParse(args[3], out var b) ? b : ef + 199;
        var feetInch = new System.Text.RegularExpressions.Regex(@"^[+\-]?\d{1,3}'\s*-?\s*\d{1,2}", System.Text.RegularExpressions.RegexOptions.Compiled);
        var metricEl = new System.Text.RegularExpressions.Regex(@"^[+\-]\d{2,3}[.,]\d{2,3}$", System.Text.RegularExpressions.RegexOptions.Compiled);
        for (int pg = ef; pg <= el; pg++)
        {
            VectorPageReader.PageContent pc; try { pc = VectorPageReader.ReadPage(args[1], pg); } catch { break; }
            if (pc.Words.Count == 0) continue;
            var elevToks = pc.Words.Where(t => { var u = (t.Text ?? "").Trim().ToUpperInvariant();
                return feetInch.IsMatch(u) || metricEl.IsMatch(u) || u is "EL" or "EL." or "ELEV" or "ELEV." or "T.O." or "T/O"; }).ToList();
            string despaced = string.Concat(string.Join(" ", pc.Words.Select(w => w.Text)).ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)));
            bool ftf = despaced.Contains("FLOORTOFLOOR") || despaced.Contains("STOREYHEIGHT") || despaced.Contains("STORYHEIGHT") || despaced.Contains("TYP.FLR") || despaced.Contains("FLR.TOFLR");
            var ladder = ScheduleGridReader.ReadLevelLadder(pc);
            if (elevToks.Count == 0 && !ftf && ladder.Count == 0) continue;
            Console.WriteLine($"p{pg}: {elevToks.Count} elevation-pattern token(s){(ftf ? "  [has FLOOR-TO-FLOOR/STOREY note]" : "")}{(ladder.Count > 0 ? $"  [level ladder: {ladder.Count} rows]" : "")}");
            // MEASUREMENT (brief 28): on an AS NOTED sheet every view carries its own scale caption; where
            // they sit against the ladder decides which caption a ladder belongs to.
            foreach (var sn in SheetScaleReader.ScaleNotesAnywhere(pc))
                Console.WriteLine($"    scale note  fx={sn.FractionX:0.00} fy={sn.FractionY:0.00}  \"{sn.Note}\"");
            // captions with no SCALE label: a ratio-shaped run of tokens on one baseline (1/8" = 1'-0", 1 : 100)
            foreach (var t in pc.Words.Where(w => w.Text.Contains("1'-0", StringComparison.Ordinal) || Regex.IsMatch(w.Text.Trim(), @"^1\s*:\s*\d{2,4}$")))
            {
                var run = pc.Words.Where(o => Math.Abs(o.Cy - t.Cy) <= 4 && o.Cx <= t.Cx + 2 && o.Cx >= t.Cx - 90).OrderBy(o => o.Cx).Select(o => o.Text);
                Console.WriteLine($"    caption?    fx={t.Cx / pc.WidthPts:0.00} fy={t.Cy / pc.HeightPts:0.00}  \"{string.Join(" ", run)}\"");
            }
            if (ladder.Count >= 3)
            {
                var levelTokens = pc.Words.Where(w => string.Equals(w.Text, "LEVEL", StringComparison.OrdinalIgnoreCase)).ToList();
                Console.WriteLine($"    ladder x: LEVEL tokens at fx {string.Join(",", levelTokens.Select(t => (t.Cx / pc.WidthPts).ToString("0.00")).Distinct().OrderBy(s => s))}; rows fy {ladder.Min(r => r.Y) / pc.HeightPts:0.00}..{ladder.Max(r => r.Y) / pc.HeightPts:0.00}");
            }
            foreach (var t in elevToks.Take(12)) Console.WriteLine($"    elev  fx={t.Cx / pc.WidthPts:0.00} fy={t.Cy / pc.HeightPts:0.00}  \"{t.Text}\"");
            if (ladder.Count >= 3)
                foreach (var r in ladder.Take(6))
                {
                    var rowToks = pc.Words.Where(w => Math.Abs(w.Cy - r.Y) <= 7).OrderBy(w => w.Cx).Select(w => w.Text);
                    Console.WriteLine($"    row {r.Normalized,-8}: {string.Join(" | ", rowToks)}");
                }
            // MEASUREMENT (2026-09-08): an elevation drawn to scale carries its storey heights as the distance
            // between consecutive level lines. The ladder gives the level lines' y; the sheet's stated scale
            // turns the distance into millimetres. Printed here, before any reader, to be compared with the
            // reference model's storeys and the dimension strings on the same sheet.
            string? scaleNote = null;
            try { scaleNote = SheetScaleReader.FromPage(pc); } catch { }
            if (scaleNote is null) { try { scaleNote = SheetScaleReader.RatioOf(TitleBlockFields.Read(pc)["SCALE"]); } catch { } }
            double? mmPerPt = scaleNote is null ? null : PlanGeometry.MetresPerPixel(scaleNote, 72) is double mpp && mpp > 0 ? mpp * 1000.0 : null;
            if (ladder.Count >= 3 && mmPerPt is double k)
            {
                var rows = ladder.OrderByDescending(r => r.Y).ToList();
                Console.WriteLine($"    storey heights from the ladder at \"{scaleNote}\" (top → bottom):");
                for (int i = 0; i + 1 < rows.Count; i++)
                {
                    double mm = (rows[i].Y - rows[i + 1].Y) * k;
                    double inches = mm / 25.4;
                    Console.WriteLine($"      {rows[i].Normalized,-10} → {rows[i + 1].Normalized,-10} {mm,8:0} mm  {Math.Floor(inches / 12):0}'-{inches % 12:0.#}\"");
                }
            }
        }
        return 0;
    }
}
