// The takeoff verb `sched-border`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// Vector front-end probe: read the NATIVE vector text + geometry of one drawing page straight from the
// PDF — no raster, no OCR — and print what came out. Proves the exact-data foundation of the takeoff.
// Usage: takeoff vector-dump <pdf> <page>
// DIAGNOSTIC: takeoff sched-border <pdf> <page> — what MarkRowScheduleReader's border route sees.
// For every schedule heading on the page and each reader's options (column, footing, shear wall):
// the border found under it or "no border", its column rules, and every row band with the text of
// its mark cell and its other cells. This is how to LOOK at a table read before believing a count;
// a row that is lost is lost in one of these lines.
internal static class SchedBorderVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("sched-border", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: takeoff sched-border <pdf> <page>"); return 1; }
        if (!File.Exists(args[1])) { Console.Error.WriteLine($"PDF not found '{args[1]}'."); return 2; }
        if (!int.TryParse(args[2], out int sbPage) || sbPage < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }

        var sbPc = VectorPageReader.ReadPage(args[1], sbPage);
        var sbRules = ScheduleTableBorder.RulesOn(sbPc);
        Console.WriteLine($"Page {sbPc.PageNumber}: {sbPc.WidthPts:F0}x{sbPc.HeightPts:F0} pts, {sbRules.Horizontal.Count} horizontal and {sbRules.Vertical.Count} vertical rules (pieces merged)");

        // what the classifier will refuse to read as structure on this sheet
        var sbNumber = SheetTitleReader.SheetNumber(sbPc);
        Console.WriteLine(sbNumber is { } n
            ? $"Sheet number \"{n.Text}\" @ {n.Cx:F0},{n.Cy:F0} (h {n.Height:F1})"
            : "Sheet number: none found in the bottom-right region, so no title block is excluded");
        var sbSet = SheetFurniture.On(sbPc);
        foreach (var region in sbSet.Regions)
            Console.WriteLine($"  furniture  {region.Kind,-44} x {region.MinX:F0}..{region.MaxX:F0}  y {region.MinY:F0}..{region.MaxY:F0}");
        Console.WriteLine($"  underlines {sbSet.Underlines.Count}; grid axes {sbSet.VerticalAxesX.Count} vertical, {sbSet.HorizontalAxesY.Count} horizontal; " +
                          $"declared column sizes {sbSet.DeclaredColumnSizesMm.Count}: {string.Join(", ", sbSet.DeclaredColumnSizesMm.Select(s => $"{s.W:0}x{s.D:0}"))}");
        var sbGrid = GridBubbles.On(sbPc);
        Console.WriteLine($"  sheet title: {SheetTitleReader.FromPage(sbPc)?.Raw ?? "(no storey parsed)"}   title text: {SheetTitleReader.TitleText(sbPc) ?? "(none)"}");
        var sbFields = TitleBlockFields.Read(sbPc);
        Console.WriteLine($"  title block fields {sbFields.Count}: " + string.Join(" | ", sbFields.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} = {kv.Value}")));
        Console.WriteLine($"  labelled circles {sbGrid.Bubbles.Count}, on a grid axis {sbGrid.Bubbles.Count(b => b.IsGridBubble)}: " +
                          string.Join(" ", sbGrid.Bubbles.Where(b => b.IsGridBubble).Select(b => b.Label).Distinct().OrderBy(l => l)));

        foreach (var sbOptions in new[] { MarkRowScheduleReader.ColumnDefaults(), MarkRowScheduleReader.FootingDefaults(), MarkRowScheduleReader.ShearWallDefaults() })
        {
            Console.WriteLine();
            Console.WriteLine($"── {sbOptions.RulePrefix} ──");
            foreach (var h in MarkRowScheduleReader.SchedulesOn(sbPc, sbOptions))
            {
                if (!h.IsTarget) { Console.WriteLine($"  \"{h.Title}\" @ {h.X:F0},{h.Y:F0}  not a target"); continue; }
                var border = ScheduleTableBorder.Under(sbPc, h.TitleMinX, h.TitleMaxX, h.TitleMinY, h.TitleHeight, sbRules);
                if (border is null)
                {
                    Console.WriteLine($"  \"{h.Title}\" @ {h.X:F0},{h.Y:F0}  title x {h.TitleMinX:F0}..{h.TitleMaxX:F0} bottom {h.TitleMinY:F0}  NO BORDER (band fallback)");
                    continue;
                }
                Console.WriteLine($"  \"{h.Title}\" @ {h.X:F0},{h.Y:F0}  border x {border.MinX:F0}..{border.MaxX:F0} y {border.MinY:F0}..{border.MaxY:F0} ({border.Width:F0}x{border.Height:F0})");
                Console.WriteLine($"     column rules x: {string.Join(", ", border.ColumnRuleXs.Select(x => x.ToString("F0")))}");
                double? markRight = border.ColumnRuleXs.Count > 0 ? border.ColumnRuleXs[0] : null;
                var inside = sbPc.Words.Where(w => border.Contains(w.Cx, w.Cy)).ToList();
                foreach (var (top, bottom) in border.RowBands())
                {
                    var band = inside.Where(w => w.Cy < top && w.Cy > bottom).OrderByDescending(w => w.Cy).ThenBy(w => w.Cx).ToList();
                    var markCell = markRight is double r ? band.Where(w => w.Cx < r).ToList() : band.Take(1).ToList();
                    string mark = string.Join(" ", markCell.OrderByDescending(w => w.Cy).ThenBy(w => w.MinX).Select(w => w.Text));
                    string rest = string.Join(" ", border.InReadingOrder(band.Except(markCell)).Select(w => w.Text));
                    if (rest.Length > 100) rest = rest[..100] + "…";
                    string kind = border.IsRuledRow(top, bottom) ? "row " : "cell";
                    Console.WriteLine($"     {kind} y {top,7:F1}..{bottom,7:F1}  mark [{mark,-10}] | {rest}");
                }
            }

            var read = MarkRowScheduleReader.ReadSchedule(sbPc, sbOptions);
            Console.WriteLine($"  read {read.Count} row(s): {string.Join(", ", read.Select(r => $"{r.Mark}[{r.Route}]"))}");
        }
        return 0;
    }
}
