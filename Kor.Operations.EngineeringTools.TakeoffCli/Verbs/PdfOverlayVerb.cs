// The takeoff verb `pdf-overlay`, as it stood in Program.cs before the split (WP3, 2026-09-11); its body is unchanged.
// OVERLAY what the intake extracted on the page it extracted it from, so a gap is seen rather than
// counted: slabs grey, columns blue, leftover lines red, mark-shaped words green. The two pictures
// that found the missing WALL layer on 2026-09-08 were made by hand; this is that picture in one verb.
// Usage: takeoff pdf-overlay <pdf> <page> <out.png> --scale N [--dpi 40] [--walls] [--columns] [--rules-db <conn>] [--mark x y]... [--crop x y halfW halfH]
internal static class PdfOverlayVerb
{
    public static bool Matches(string[] args) => args.Length >= 1 && args[0].Equals("pdf-overlay", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("Usage: takeoff pdf-overlay <pdf> <page> <out.png> --scale N [--dpi 40] [--walls] [--columns] [--rules-db <conn>] [--tendons] [--mark x y]... [--crop x y halfW halfH]"); return 1; }
        string ovPdf = args[1], ovOut = args[3];
        if (!File.Exists(ovPdf)) { Console.Error.WriteLine($"PDF not found '{ovPdf}'."); return 2; }
        if (!int.TryParse(args[2], out int ovPage) || ovPage < 1) { Console.Error.WriteLine("Page must be a positive integer."); return 2; }
        int ovScale = 0; double ovDpi = 40; string? ovRules = null;
        for (int i = 4; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[++i], out ovScale);
            else if (a.Equals("--dpi", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out ovDpi);
            else if (a.Equals("--rules-db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) ovRules = args[++i];
        }
        if (ovScale <= 0) { Console.Error.WriteLine("--scale <denominator> is required (1/8\" = 1'-0\" is 96); the overlay must not assume one."); return 2; }
        var (ovOptions, _) = PdfIntakeOptions.For(ovRules);

        // with --walls, the classifier says why each long pair of cut-pen lines was or was not a wall
        var ovFaceTrace = new List<string>();
        if (args.Any(a => a.Equals("--walls", StringComparison.OrdinalIgnoreCase))) GeometryFilterService.FaceTrace = ovFaceTrace.Add;
        var ovGeo = PdfPlanReader.Read(ovPdf, ovScale, ovPage, ovOptions, annotationsOnly: false);
        GeometryFilterService.FaceTrace = null;
        var ovContent = VectorPageReader.ReadPage(ovPdf, ovPage);
        using var ovImg = PlanPdfRenderer.RenderPage(ovPdf, ovPage, ovDpi);
        double mmToPt = 1.0 / (ovScale * PdfToSafeConstants.PointsToMm);
        double px = ovDpi / 72.0;
        (int X, int Y) ToPixel(double xMm, double yMm) => ((int)Math.Round(xMm * mmToPt * px), (int)Math.Round((ovGeo.PageHeightPts - yMm * mmToPt) * px));
        void OvPlot(int x, int y, Rgba32 c, int weight)
        {
            for (int dx = -weight; dx <= weight; dx++)
                for (int dy = -weight; dy <= weight; dy++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && yy >= 0 && xx < ovImg.Width && yy < ovImg.Height) ovImg[xx, yy] = c;
                }
        }
        void OvLine((int X, int Y) a, (int X, int Y) b, Rgba32 c, int weight)
        {
            int steps = Math.Max(1, Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)));
            for (int s = 0; s <= steps; s++)
            {
                double f = (double)s / steps;
                OvPlot((int)Math.Round(a.X + (b.X - a.X) * f), (int)Math.Round(a.Y + (b.Y - a.Y) * f), c, weight);
            }
        }
        void OvPoly(IReadOnlyList<(double X, double Y)> pts, Rgba32 c, int weight, bool close)
        {
            for (int i = 1; i < pts.Count; i++) OvLine(ToPixel(pts[i - 1].X, pts[i - 1].Y), ToPixel(pts[i].X, pts[i].Y), c, weight);
            if (close && pts.Count > 2) OvLine(ToPixel(pts[^1].X, pts[^1].Y), ToPixel(pts[0].X, pts[0].Y), c, weight);
        }
        var grey = new Rgba32(110, 110, 110); var blue = new Rgba32(30, 70, 220); var red = new Rgba32(220, 40, 40); var green = new Rgba32(0, 160, 60);
        foreach (var line in ovGeo.Lines) OvPoly(line, red, 0, close: false);
        foreach (var slab in ovGeo.Slabs) OvPoly(slab, grey, 1, close: true);
        // walls read from two face lines (step 20) purple; they are appended after the filled ones
        int ovFirstFaceWall = ovGeo.FirstFaceWall;
        for (int wi = 0; wi < ovGeo.Walls.Count; wi++)
            OvPoly(ovGeo.Walls[wi].Outline, wi >= ovFirstFaceWall ? new Rgba32(150, 0, 200) : new Rgba32(130, 0, 0), 2, close: true);
        // doorways knocked out of walls, yellow, across the opening
        foreach (var d in ovGeo.Doorways) OvPoly(new[] { d.Start, d.End }, new Rgba32(230, 180, 0), 3, close: false);
        // named grid axes, cyan, across the whole page
        double ovPageWmm = ovContent.WidthPts * ovScale * PdfToSafeConstants.PointsToMm, ovPageHmm = ovContent.HeightPts * ovScale * PdfToSafeConstants.PointsToMm;
        foreach (var axis in ovGeo.GridAxes)
            OvPoly(axis.Vertical ? new[] { (axis.AtMm, 0.0), (axis.AtMm, ovPageHmm) } : new[] { (0.0, axis.AtMm), (ovPageWmm, axis.AtMm) }, new Rgba32(0, 170, 190), 1, close: false);
        // a footing no label on the plan names is drawn magenta and listed, and so is every placed
        // footing label no footing answers — the two ways the read and the plan disagree
        var magenta = new Rgba32(200, 0, 200);
        foreach (var footing in ovGeo.Footings) OvPoly(footing.Outline, footing.LabelledOnThePlan ? new Rgba32(230, 120, 0) : magenta, 2, close: true);
        var unlabelled = ovGeo.Footings.Where(f => !f.LabelledOnThePlan).ToList();
        var unanswered = new List<string>();
        try
        {
            var (ftypes, fbox) = FootingScheduleReader.ReadSchedule(ovContent);
            double sMm0 = ovScale * PdfToSafeConstants.PointsToMm;
            var ovFurniture = SheetFurniture.On(ovContent, PlanAgreesWithItsSchedule.DefaultToleranceMm);
            foreach (var (mark, positions) in FootingScheduleReader.PlacementPositions(ovContent, ftypes, fbox, ovFurniture))
                foreach (var (lpx, lpy) in positions)
                {
                    double lx = lpx * sMm0, ly = lpy * sMm0;
                    bool answered = ovGeo.Footings.Any(f =>
                    {
                        double x0 = f.Outline.Min(p => p.X), x1 = f.Outline.Max(p => p.X), y0 = f.Outline.Min(p => p.Y), y1 = f.Outline.Max(p => p.Y);
                        double dx = Math.Max(Math.Max(x0 - lx, 0), lx - x1), dy = Math.Max(Math.Max(y0 - ly, 0), ly - y1);
                        return string.Equals(f.Mark, mark, StringComparison.OrdinalIgnoreCase) && Math.Sqrt(dx * dx + dy * dy) <= Math.Max(f.LengthMm, f.WidthMm) / 2;
                    });
                    if (!answered && ftypes.Any(t => t.Mark.Equals(mark, StringComparison.OrdinalIgnoreCase) && t.IsSpread))
                        unanswered.Add($"{mark} at ({lx:0},{ly:0}) mm");
                }
        }
        catch { /* no foundation schedule on this sheet */ }
        // a tendon's anchor is not a column (step 48): the intake stands them down after the classifier; so does this picture
        var ovTendons = TendonAnchors.Read(ovContent, ovGeo, ovScale * PdfToSafeConstants.PointsToMm, ovOptions.ForceWords);
        IReadOnlyList<bool>? ovDeclared = null;
        try
        {
            var ovSchedule = ColumnScheduleReader.ReadSchedule(ovContent);
            if (ovSchedule.Count > 0 && ovGeo.Columns.Count > 0)
                ovDeclared = PlanAgreesWithItsSchedule.Check(ovGeo, ovSchedule, ovContent, ovOptions.AgreementToleranceMm, ovOptions.AgreementLabelReachMm).Columns.Select(c => c.SizeIsDeclaredSomewhere).ToList();
        }
        catch { /* no column schedule the reader can use: every column is undeclared */ }
        int ovAnchors = TendonAnchors.StandDownColumns(ovGeo, ovTendons, ovDeclared);
        var anchorOrange = new Rgba32(240, 140, 0);
        for (int i = 0; i < ovGeo.Columns.Count; i++)
        {
            var (cx, cy) = ovGeo.Columns[i];
            var (w, d) = i < ovGeo.ColumnSizes.Count ? ovGeo.ColumnSizes[i] : (0.0, 0.0);
            bool anchor = i < ovGeo.ColumnIsTendonAnchor.Count && ovGeo.ColumnIsTendonAnchor[i];
            var colour = anchor ? anchorOrange : blue;
            if (w > 0 && d > 0)
                OvPoly(new[] { (cx - w / 2, cy - d / 2), (cx + w / 2, cy - d / 2), (cx + w / 2, cy + d / 2), (cx - w / 2, cy + d / 2) }, colour, 1, close: true);
            var c = ToPixel(cx, cy); OvPlot(c.X, c.Y, colour, 2);
        }
        if (ovTendons.Count > 0) Console.WriteLine($"  tendons {ovTendons.Count} (a line labelled with a force); {ovAnchors} column-sized shape(s) at their ends are anchors, not columns (orange)");
        // --tendons: each tendon's ends and label, and for every column-sized shape the nearest tendon end - the
        // census that says why an anchor was or was not stood down (step 48)
        if (args.Any(a => a.Equals("--tendons", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var t in ovTendons)
                Console.WriteLine($"    tendon '{t.Label}' ({t.Start.X:0},{t.Start.Y:0}) -> ({t.End.X:0},{t.End.Y:0}) mm, {Math.Sqrt((t.End.X - t.Start.X) * (t.End.X - t.Start.X) + (t.End.Y - t.Start.Y) * (t.End.Y - t.Start.Y)):0} mm long");
            for (int i = 0; i < ovGeo.Columns.Count; i++)
            {
                var (cx, cy) = ovGeo.Columns[i];
                var (w, d) = i < ovGeo.ColumnSizes.Count ? ovGeo.ColumnSizes[i] : (0.0, 0.0);
                double nearest = ovTendons.Count == 0 ? double.NaN : ovTendons.Min(t => Math.Min(Math.Sqrt((t.Start.X - cx) * (t.Start.X - cx) + (t.Start.Y - cy) * (t.Start.Y - cy)), Math.Sqrt((t.End.X - cx) * (t.End.X - cx) + (t.End.Y - cy) * (t.End.Y - cy))));
                bool anchor = i < ovGeo.ColumnIsTendonAnchor.Count && ovGeo.ColumnIsTendonAnchor[i];
                Console.WriteLine($"    column {w / 25.4:0}x{d / 25.4:0} in at ({cx:0},{cy:0}): nearest tendon end {nearest:0} mm{(anchor ? "  ANCHOR" : "")}");
            }
        }
        // the pattern cells that left the columns (step 37), orange: a person can see they were a fill
        // pattern along a wall and not two members touching
        var orange = new Rgba32(240, 140, 0);
        foreach (var (centre, w, d) in ovGeo.PatternCells)
            if (w > 0 && d > 0)
                OvPoly(new[] { (centre.X - w / 2, centre.Y - d / 2), (centre.X + w / 2, centre.Y - d / 2), (centre.X + w / 2, centre.Y + d / 2), (centre.X - w / 2, centre.Y + d / 2) }, orange, 1, close: true);
        int marks = 0;
        foreach (var wd in ovContent.Words)
        {
            if (DrawingIntake.KindOf(wd.Text) != "mark") continue;
            marks++;
            double sMm = ovScale * PdfToSafeConstants.PointsToMm;
            OvPoly(new[] { (wd.MinX * sMm, wd.MinY * sMm), (wd.MaxX * sMm, wd.MinY * sMm), (wd.MaxX * sMm, wd.MaxY * sMm), (wd.MinX * sMm, wd.MaxY * sMm) }, green, 0, close: true);
        }
        // the furniture regions, light blue: what the classifier discards as furniture (schedules,
        // titled boxes, the title block, the north arrow) — so a table whose rules still reach the
        // DXF as lines can be seen standing outside its region
        var lightBlue = new Rgba32(90, 170, 230);
        double sMmR = ovScale * PdfToSafeConstants.PointsToMm;
        var ovSet = SheetFurniture.On(ovContent, PlanAgreesWithItsSchedule.DefaultToleranceMm);
        var ovRegions = ovSet.Regions;
        // the plan views the sheet titles (step 26): each underlined title naming a plan, with where it sits
        var ovViews = SheetViews.Titles(ovContent);
        Console.WriteLine($"  views {ovViews.Count}: {string.Join("; ", ovViews.Select(v => $"\"{v.Title}\" under x {v.MinXPts:0}-{v.MaxXPts:0} pt at y {v.YPts:0}"))}");
        foreach (var r in ovRegions)
            OvPoly(new[] { (r.MinX * sMmR, r.MinY * sMmR), (r.MaxX * sMmR, r.MinY * sMmR), (r.MaxX * sMmR, r.MaxY * sMmR), (r.MinX * sMmR, r.MaxY * sMmR) }, lightBlue, 1, close: true);
        Console.WriteLine($"  furniture regions {ovRegions.Count} (light blue): {string.Join("; ", ovRegions.Select(r => r.Kind).Take(14))}");
        // the match line a plan too wide for one sheet was split on (step 22), magenta
        var ovMatch = SheetFurniture.MatchLines(ovContent).ToList();
        foreach (var m in ovMatch) OvPoly(new[] { (m.X0 * sMmR, m.Y0 * sMmR), (m.X1 * sMmR, m.Y1 * sMmR) }, new Rgba32(220, 0, 160), 3, close: false);
        var ovMatchWords = ovContent.Words.Where(w => w.Text.Trim().StartsWith("MATCH", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var w in ovMatchWords)
        {
            var lineWord = ovContent.Words.FirstOrDefault(o => o.Text.Trim().Equals("LINE", StringComparison.OrdinalIgnoreCase) && Math.Abs(o.Cx - w.Cx) <= 4 * w.Height && Math.Abs(o.Cy - w.Cy) <= 4 * w.Height);
            Console.WriteLine($"    '{w.Text}' at ({w.Cx:0},{w.Cy:0}) pt, box {w.Width:0.0} x {w.Height:0.0} pt" + (lineWord.Text is not null ? $"; LINE at ({lineWord.Cx:0},{lineWord.Cy:0}) box {lineWord.Width:0.0} x {lineWord.Height:0.0}" : "; no LINE beside it"));
        }
        foreach (var m in ovMatch) Console.WriteLine($"    match line ({m.X0:0},{m.Y0:0})-({m.X1:0},{m.Y1:0}) pt, {(Math.Abs(m.X1 - m.X0) > Math.Abs(m.Y1 - m.Y0) ? "horizontal" : "vertical")}");
        Console.WriteLine($"  match lines {ovMatch.Count} (magenta){(ovMatch.Count == 0 && ovMatchWords.Count > 0 ? $"; the word MATCH appears {ovMatchWords.Count} time(s) at {string.Join(", ", ovMatchWords.Take(3).Select(w => $"({w.Cx:0},{w.Cy:0}) pt '{w.Text}'"))} but no line spanning the drawing runs beside it" : "")}");
        // --mark x y (page mm, repeatable): a ring where model-to-page said a point is; --crop x y halfW halfH
        // (page mm): the picture cut to that neighbourhood, so the thing is looked at rather than counted.
        // From docs/etabs-handoff/crop_mm.py (WP2, 2026-09-11).
        var ovMarks = new List<(double X, double Y)>();
        (double X, double Y, double HalfW, double HalfH)? ovCrop = null;
        for (int i = 4; i < args.Length; i++)
        {
            if (args[i].Equals("--mark", StringComparison.OrdinalIgnoreCase) && i + 2 < args.Length)
                ovMarks.Add((OvNum(args[i + 1]), OvNum(args[i + 2])));
            else if (args[i].Equals("--crop", StringComparison.OrdinalIgnoreCase) && i + 4 < args.Length)
                ovCrop = (OvNum(args[i + 1]), OvNum(args[i + 2]), OvNum(args[i + 3]), OvNum(args[i + 4]));
        }
        foreach (var (mx, my) in ovMarks)
        {
            var c = ToPixel(mx, my);
            int r = Math.Max(6, (int)Math.Round(300 * mmToPt * px));           // a 600 mm ring, never smaller than 12 px
            for (int deg = 0; deg < 360; deg += 2)
                OvPlot(c.X + (int)Math.Round(r * Math.Cos(deg * Math.PI / 180)), c.Y + (int)Math.Round(r * Math.Sin(deg * Math.PI / 180)), new Rgba32(255, 0, 0), 2);
            Console.WriteLine($"  mark at ({mx:0}, {my:0}) mm -> pixel ({c.X}, {c.Y})");
        }
        if (ovCrop is { } cr)
        {
            var lo = ToPixel(cr.X - cr.HalfW, cr.Y + cr.HalfH); var hi = ToPixel(cr.X + cr.HalfW, cr.Y - cr.HalfH);
            int x0 = Math.Clamp(lo.X, 0, ovImg.Width - 1), y0 = Math.Clamp(lo.Y, 0, ovImg.Height - 1);
            int x1 = Math.Clamp(hi.X, x0 + 1, ovImg.Width), y1 = Math.Clamp(hi.Y, y0 + 1, ovImg.Height);
            ovImg.Mutate(m => m.Crop(new Rectangle(x0, y0, x1 - x0, y1 - y0)));
            Console.WriteLine($"  cropped to ({cr.X - cr.HalfW:0}..{cr.X + cr.HalfW:0}, {cr.Y - cr.HalfH:0}..{cr.Y + cr.HalfH:0}) mm = pixels ({x0},{y0})-({x1},{y1})");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ovOut)) ?? ".");
        ovImg.SaveAsPng(ovOut);
        Console.WriteLine($"{Path.GetFileName(ovPdf)} p{ovPage} 1:{ovScale} @ {ovDpi} dpi → {ovOut}");
        Console.WriteLine($"  slabs {ovGeo.Slabs.Count} (grey)   columns {ovGeo.Columns.Count - ovAnchors} (blue)   walls {ovGeo.Walls.Count} (dark red)   footings {ovGeo.Footings.Count} (orange; {unlabelled.Count} without a label, magenta)   grid axes {ovGeo.GridAxes.Count} (cyan: X {string.Join(",", ovGeo.GridAxes.Where(a => a.Vertical).Select(a => a.Name))}; Y {string.Join(",", ovGeo.GridAxes.Where(a => !a.Vertical).Select(a => a.Name))})   lines {ovGeo.Lines.Count} (red)   mark-shaped words {marks} (green)");
        foreach (var f in unlabelled) Console.WriteLine($"  footing {f.Mark} at ({f.Centre.X:0},{f.Centre.Y:0}) mm: no label on the plan names it");
        foreach (var u in unanswered) Console.WriteLine($"  label {u}: no footing read answers it");
        if (ovGeo.Doorways.Count > 0) Console.WriteLine($"  doorways {ovGeo.Doorways.Count} (yellow): openings knocked out of walls with paper fills; those walls are their piers");
        // --columns: the column reads as a census — size (short x long, to the inch), pen, and how many of
        // that size stand shoulder to shoulder with a twin (a pattern's cells abut; a column stands alone).
        // Built for 31170's P1 plan, where 311 "columns" were the cells of the walls' fill pattern (step 37).
        if (args.Any(a => a.Equals("--columns", StringComparison.OrdinalIgnoreCase)) && ovGeo.Columns.Count > 0)
        {
            var cols = ovGeo.Columns.Select((c, i) => (Centre: c, Size: i < ovGeo.ColumnSizes.Count ? ovGeo.ColumnSizes[i] : (0.0, 0.0),
                                                      Pen: i < ovGeo.ColumnColors.Count ? $"#{ovGeo.ColumnColors[i].R:X2}{ovGeo.ColumnColors[i].G:X2}{ovGeo.ColumnColors[i].B:X2}" : "?")).ToList();
            static (int, int) Inches((double W, double D) s) => ((int)Math.Round(Math.Min(s.W, s.D) / 25.4), (int)Math.Round(Math.Max(s.W, s.D) / 25.4));
            int abutting = 0;
            var abuts = new bool[cols.Count];
            for (int i = 0; i < cols.Count; i++)
                for (int j = 0; j < cols.Count && !abuts[i]; j++)
                {
                    if (i == j || Inches(cols[i].Size) != Inches(cols[j].Size)) continue;
                    double dx = Math.Abs(cols[i].Centre.X - cols[j].Centre.X), dy = Math.Abs(cols[i].Centre.Y - cols[j].Centre.Y);
                    double w = cols[i].Size.Item1, d = cols[i].Size.Item2;
                    bool alongX = Math.Abs(dx - w) <= 25 && dy <= 25, alongY = Math.Abs(dy - d) <= 25 && dx <= 25;
                    if (alongX || alongY) { abuts[i] = true; abutting++; }
                }
            Console.WriteLine($"  columns by size (--columns): {cols.Count} read, {abutting} standing edge to edge with a twin of the same size; {ovGeo.PatternCells.Count} pattern cell(s) already left the columns (orange)");
            foreach (var (centre, w, d) in ovGeo.PatternCells.Take(40))
                Console.WriteLine($"    cell {Inches((w, d)).Item1,3} x {Inches((w, d)).Item2,3} in at ({centre.X:0}, {centre.Y:0}) mm");
            if (ovGeo.PatternCells.Count > 40) Console.WriteLine($"    ... and {ovGeo.PatternCells.Count - 40} more cells");
            foreach (var g in cols.Select((c, i) => (c, i)).GroupBy(x => (Inches(x.c.Size), x.c.Pen)).OrderByDescending(g => g.Count()).Take(12))
                Console.WriteLine($"    {g.Key.Item1.Item1,3} x {g.Key.Item1.Item2,3} in  {g.Key.Pen}: {g.Count(),4}, of which {g.Count(x => abuts[x.i]),4} abut a twin");
        }
        // --agreement: the plan's self-check column by column — size, the nearest mark label and how far, and
        // whether the schedule's size for that mark is this column's. The coverage ratchet in FiveStickFilesTests
        // fails on a number; this is the list behind the number (built 2026-09-10 when the centroid fix moved
        // 31130 p12 from 25 to 22 of 45 and the question was which three, and why).
        if (args.Any(a => a.Equals("--agreement", StringComparison.OrdinalIgnoreCase)))
        {
            var declaredRows = ColumnScheduleReader.ReadSchedule(ovContent);
            // --reach N overrides the label reach for the listing, so a floor that moved can be asked "how far are the labels"
            double ovReach = ovOptions.AgreementLabelReachMm;
            int reachAt = Array.FindIndex(args, a => a.Equals("--reach", StringComparison.OrdinalIgnoreCase));
            if (reachAt >= 0 && reachAt + 1 < args.Length && double.TryParse(args[reachAt + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double r)) ovReach = r;
            var agreement = PlanAgreesWithItsSchedule.Check(ovGeo, declaredRows, ovContent, ovOptions.AgreementToleranceMm, ovReach);
            Console.WriteLine($"  agreement (--agreement): {agreement.ColumnsFound} columns, {agreement.LabelsOnThePlan} labels on the plan, {agreement.MatchedToTheirOwnMark} match their own mark's size, {agreement.AttributedToAMark} have a mark within {ovReach:0} mm; never found: {string.Join(",", agreement.MarksDeclaredButNeverFound)}");
            foreach (var c in agreement.Columns.OrderBy(c => c.SizeMatchesItsOwnMark).ThenBy(c => c.NearestMark))
                Console.WriteLine($"    {(c.SizeMatchesItsOwnMark ? "own " : c.NearestMark is null ? "none" : "MISS")} {Math.Min(c.WidthMm, c.DepthMm) / 25.4,3:0} x {Math.Max(c.WidthMm, c.DepthMm) / 25.4,3:0} in at ({c.XMm:0},{c.YMm:0}) mm  nearest {c.NearestMark ?? "-",-6} {(c.NearestMarkDistanceMm is double dd ? $"{dd:0} mm" : "")}{(c.SizeIsDeclaredSomewhere ? "" : "  (size declared nowhere)")}");
        }
        static double OvWallLen(WallPanel w) => Math.Sqrt(Math.Pow(w.End.X - w.Start.X, 2) + Math.Pow(w.End.Y - w.Start.Y, 2));
        // --walls: the filled walls as a census too — thickness, pen colour, count and length range — so a
        // stall symbol or a legend box read as a wall shows up as a size no wall has (step 38)
        if (args.Any(a => a.Equals("--walls", StringComparison.OrdinalIgnoreCase)) && ovFirstFaceWall > 0)
        {
            Console.WriteLine($"  filled walls {ovFirstFaceWall} (dark red), by thickness and colour:");
            foreach (var g in ovGeo.Walls.Take(ovFirstFaceWall).Select((w, i) => (w, c: i < ovGeo.WallColors.Count ? $"#{ovGeo.WallColors[i].R:X2}{ovGeo.WallColors[i].G:X2}{ovGeo.WallColors[i].B:X2}" : "?"))
                                         .GroupBy(x => ((int)Math.Round(x.w.ThicknessMm / 25.4), x.c)).OrderBy(g => g.Key.Item1))
                Console.WriteLine($"    {g.Key.Item1,3} in thick {g.Key.c}: {g.Count(),4} wall(s), length {g.Min(x => OvWallLen(x.w)) / 25.4:0}-{g.Max(x => OvWallLen(x.w)) / 25.4:0} in");
        }
        if (ovGeo.Walls.Count > ovFirstFaceWall)
        {
            var faceWalls = ovGeo.Walls.Skip(ovFirstFaceWall).ToList();
            Console.WriteLine($"  walls from two face lines {faceWalls.Count} (purple): parallel lines a wall's thickness apart, alone, overlapping a wall's length (step 20)");
            foreach (var g in faceWalls.GroupBy(w => (int)Math.Round(w.ThicknessMm / 25.4)).OrderBy(g => g.Key))
                Console.WriteLine($"    {g.Key,3} in thick: {g.Count(),4} wall(s), length {g.Min(OvWallLen) / 25.4:0}-{g.Max(OvWallLen) / 25.4:0} in");
            if (args.Any(a => a.Equals("--walls", StringComparison.OrdinalIgnoreCase)))
                foreach (var (w, wi) in faceWalls.Select((w, i) => (w, i + ovFirstFaceWall)).OrderByDescending(x => OvWallLen(x.w)))
                {
                    var faces = ovGeo.WallFaceLines.Where(kv => kv.Value == wi).Select(kv => kv.Key).OrderBy(k => k).ToList();
                    string pens = string.Join(" + ", faces.Select(k => $"#{ovGeo.LineColors[k].R:X2}{ovGeo.LineColors[k].G:X2}{ovGeo.LineColors[k].B:X2} w{ovGeo.LineWidths[k]:0.00}"));
                    // every other long stroked line parallel to the wall within three thicknesses of its axis:
                    // offset from the axis in inches and its pen, so a footing edge or a hatch beside a face shows
                    double ax = w.End.X - w.Start.X, ay = w.End.Y - w.Start.Y, al = Math.Sqrt(ax * ax + ay * ay); ax /= al; ay /= al;
                    double nx = -ay, ny = ax;
                    var near = new List<string>();
                    for (int li = 0; li < ovGeo.Lines.Count; li++)
                    {
                        var l = ovGeo.Lines[li];
                        if (l.Count != 2 || faces.Contains(li)) continue;
                        double lx = l[1].X - l[0].X, ly = l[1].Y - l[0].Y, ll = Math.Sqrt(lx * lx + ly * ly);
                        if (ll < 609.6 || Math.Abs((lx * ax + ly * ay) / ll) < 0.9998) continue;
                        double t0 = (l[0].X - w.Start.X) * ax + (l[0].Y - w.Start.Y) * ay, t1 = (l[1].X - w.Start.X) * ax + (l[1].Y - w.Start.Y) * ay;
                        if (Math.Max(t0, t1) < 0 || Math.Min(t0, t1) > al) continue;
                        double off = (l[0].X - w.Start.X) * nx + (l[0].Y - w.Start.Y) * ny;
                        if (Math.Abs(off) > 3 * w.ThicknessMm) continue;
                        near.Add($"{off / 25.4:+0.0;-0.0}\" w{ovGeo.LineWidths[li]:0.00} {ll / 25.4:0}\"");
                    }
                    // and what is drawn between the faces: every line whose midpoint lies in the wall's box,
                    // by its angle to the axis and how much of the gap it spans, so a stair's risers show
                    double wl = OvWallLen(w);
                    var inside = new List<string>();
                    for (int li = 0; li < ovGeo.Lines.Count; li++)
                    {
                        var l = ovGeo.Lines[li];
                        if (faces.Contains(li)) continue;
                        double mxl = l.Average(p => p.X), myl = l.Average(p => p.Y);
                        double t = (mxl - w.Start.X) * ax + (myl - w.Start.Y) * ay, n = (mxl - w.Start.X) * nx + (myl - w.Start.Y) * ny;
                        if (t < 0 || t > wl || Math.Abs(n) > w.ThicknessMm / 2) continue;
                        double lx = l[^1].X - l[0].X, ly = l[^1].Y - l[0].Y, ll = Math.Sqrt(lx * lx + ly * ly);
                        double cosA = ll > 0 ? Math.Abs((lx * ax + ly * ay) / ll) : 1;
                        double n0 = (l[0].X - w.Start.X) * nx + (l[0].Y - w.Start.Y) * ny, n1 = (l[^1].X - w.Start.X) * nx + (l[^1].Y - w.Start.Y) * ny;
                        inside.Add($"{(cosA > 0.98 ? "along" : cosA < 0.17 ? "square" : "diag")} {l.Count}pt {ll / 25.4:0}\" spans {Math.Abs(n1 - n0) / w.ThicknessMm:0.00} at {t / 25.4:0}\" w{ovGeo.LineWidths[li]:0.00}");
                    }
                    Console.WriteLine($"    face wall {wl / 25.4,7:0.0} x {w.ThicknessMm / 25.4,5:0.0} in   centre ({(w.Start.X + w.End.X) / 2,6:0},{(w.Start.Y + w.End.Y) / 2,6:0}) mm   faces {pens}   beside: {string.Join(", ", near)}");
                    if (inside.Count > 0) Console.WriteLine($"      between the faces ({inside.Count}): {string.Join("; ", inside.Take(12))}");
                }
        }
        if (args.Any(a => a.Equals("--walls", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"  face-line rule, every pair of cut-pen lines {GeometryFilterService.FaceTraceMinOverlapMm / 25.4:0}\"+ long ({ovFaceTrace.Count} lines):");
            foreach (var t in ovFaceTrace) Console.WriteLine($"      {t}");
            foreach (var d in ovGeo.Doorways)
                Console.WriteLine($"  doorway {d.LengthMm / 25.4,6:0.0} in wide in a {d.ThicknessMm / 25.4,4:0.0} in wall at ({(d.Start.X + d.End.X) / 2,6:0},{(d.Start.Y + d.End.Y) / 2,6:0}) mm");
            // Each wall the page read, as the draftsman would size it: length x thickness in inches at
            // the sheet's scale, and where its centre sits in page millimetres.
            // What else is painted on each wall after it: the drafter's knockouts are here, whatever
            // colour and stroke they carry, so a doorway the rule did not read can be seen for what it is.
            using var ovDoc = UglyToad.PdfPig.PdfDocument.Open(ovPdf);
            var ovRaw = PdfPlanReader.ParsePage(ovDoc.GetPage(ovPage), ovScale * PdfToSafeConstants.PointsToMm);
            // The pens: what the sheet's long lines, its filled walls' outlines and its grid axes are drawn
            // with, so a rule about "the cut pen" can be measured before it is written.
            {
                var longPens = ovRaw.Where(r => !r.IsFilled && r.IsStroked && r.Points.Count == 2
                                                && Math.Sqrt(Math.Pow(r.Points[1].X - r.Points[0].X, 2) + Math.Pow(r.Points[1].Y - r.Points[0].Y, 2)) >= 1219.2)
                                    .GroupBy(r => r.LineWidth).OrderBy(g => g.Key).Select(g => $"w{g.Key:0.00}x{g.Count()}");
                Console.WriteLine($"  pens of stroked lines 48\"+: {string.Join(" ", longPens)}");
                var wallPens = ovRaw.Where(r => r.IsFilled && r.IsStroked && ovGeo.Walls.Take(ovFirstFaceWall).Any(w =>
                                                Math.Abs(r.Points.Min(p => p.X) - w.Outline.Min(p => p.X)) < 1 && Math.Abs(r.Points.Max(p => p.Y) - w.Outline.Max(p => p.Y)) < 1))
                                    .GroupBy(r => r.LineWidth).OrderBy(g => g.Key).Select(g => $"w{g.Key:0.00}x{g.Count()}");
                Console.WriteLine($"  pens of filled walls' outlines: {string.Join(" ", wallPens)}");
                var unstrokedWalls = ovRaw.Count(r => r.IsFilled && !r.IsStroked && ovGeo.Walls.Take(ovFirstFaceWall).Any(w =>
                                                Math.Abs(r.Points.Min(p => p.X) - w.Outline.Min(p => p.X)) < 1 && Math.Abs(r.Points.Max(p => p.Y) - w.Outline.Max(p => p.Y)) < 1));
                Console.WriteLine($"  filled walls with no stroke of their own: {unstrokedWalls}");
                // the pen of the stroked lines that lie along a filled wall's long edges: the cut pen
                var edgePens = new List<double>();
                foreach (var w in ovGeo.Walls.Take(ovFirstFaceWall))
                {
                    var o = w.Outline;
                    for (int e = 0; e < o.Count; e++)
                    {
                        var p0 = o[e]; var p1 = o[(e + 1) % o.Count];
                        double ex = p1.X - p0.X, ey = p1.Y - p0.Y, el = Math.Sqrt(ex * ex + ey * ey);
                        if (el < w.ThicknessMm * 1.5) continue;                                   // a short edge is the end
                        ex /= el; ey /= el;
                        foreach (var r in ovRaw)
                        {
                            if (r.IsFilled || !r.IsStroked || r.Points.Count != 2) continue;
                            bool on = r.Points.All(q =>
                            {
                                double t = (q.X - p0.X) * ex + (q.Y - p0.Y) * ey, n = Math.Abs(-(q.X - p0.X) * ey + (q.Y - p0.Y) * ex);
                                return n < 2 && t > -2 && t < el + 2;
                            });
                            if (on && Math.Sqrt(Math.Pow(r.Points[1].X - r.Points[0].X, 2) + Math.Pow(r.Points[1].Y - r.Points[0].Y, 2)) > el / 2) edgePens.Add(r.LineWidth);
                        }
                    }
                }
                Console.WriteLine($"  pens of lines along filled walls' long edges: {string.Join(" ", edgePens.GroupBy(p => p).OrderBy(g => g.Key).Select(g => $"w{g.Key:0.00}x{g.Count()}"))}");

                // Filled four-point shapes of wall proportions the rectangle rule rejected: a wall that
                // tapers (a property-line retaining wall) is one, and this lists its faces' angle and its
                // thickness at each end so the next rule can be written against the population.
                int tapered = 0;
                foreach (var r in ovRaw)
                {
                    if (!r.IsFilled || r.IsAnnotation || r.Points.Count < 4) continue;
                    var p = r.Points;
                    int n = p.Count;
                    double bw = p.Max(q => q.X) - p.Min(q => q.X), bh = p.Max(q => q.Y) - p.Min(q => q.Y);
                    if (Math.Sqrt(bw * bw + bh * bh) >= 20000)
                    {
                        Console.WriteLine($"  long fill: {n} pts, box {bw / 25.4:0} x {bh / 25.4:0} in, colour #{r.Color.R:X2}{r.Color.G:X2}{r.Color.B:X2}, {(r.IsStroked ? "stroked" : "no stroke")}, at ({p.Average(q => q.X):0},{p.Average(q => q.Y):0}) mm, corners {string.Join(" ", p.Take(6).Select(q => $"({q.X:0},{q.Y:0})"))}");
                        // what is painted over it afterwards in paper colour, and what lines cross it
                        double fx0 = p.Min(q => q.X), fx1 = p.Max(q => q.X), fy0 = p.Min(q => q.Y), fy1 = p.Max(q => q.Y);
                        int at = ovRaw.IndexOf(r);
                        for (int k = at + 1; k < ovRaw.Count; k++)
                        {
                            var o = ovRaw[k];
                            if (!o.IsFilled || o.Points.Count < 3) continue;
                            if (!(o.Color.R >= 0xF0 && o.Color.G >= 0xF0 && o.Color.B >= 0xF0)) continue;
                            double ox0 = o.Points.Min(q => q.X), ox1 = o.Points.Max(q => q.X), oy0 = o.Points.Min(q => q.Y), oy1 = o.Points.Max(q => q.Y);
                            if (Math.Min(ox1, fx1) - Math.Max(ox0, fx0) <= 0 || Math.Min(oy1, fy1) - Math.Max(oy0, fy0) <= 0) continue;
                            Console.WriteLine($"      paper fill after it (path {k}): {o.Points.Count} pts, box {(ox1 - ox0) / 25.4:0} x {(oy1 - oy0) / 25.4:0} in, corners {string.Join(" ", o.Points.Take(5).Select(q => $"({q.X:0},{q.Y:0})"))}");
                        }
                        // and the long lines running along it: offset of each end from the band's edge, in inches, with its pen
                        bool alongY = bh > bw;
                        foreach (var o in ovRaw)
                        {
                            if (o.IsFilled || !o.IsStroked || o.Points.Count != 2) continue;
                            var q0 = o.Points[0]; var q1 = o.Points[1];
                            double len = Math.Sqrt(Math.Pow(q1.X - q0.X, 2) + Math.Pow(q1.Y - q0.Y, 2));
                            if (len < 0.25 * Math.Max(bw, bh)) continue;
                            double along = alongY ? Math.Abs(q1.Y - q0.Y) / len : Math.Abs(q1.X - q0.X) / len;
                            if (along < 0.98) continue;
                            double m0 = alongY ? Math.Min(q0.Y, q1.Y) : Math.Min(q0.X, q1.X), m1 = alongY ? Math.Max(q0.Y, q1.Y) : Math.Max(q0.X, q1.X);
                            double lo = alongY ? fy0 : fx0, hi = alongY ? fy1 : fx1;
                            if (m1 < lo || m0 > hi) continue;
                            double e0 = (alongY ? q0.X - fx0 : q0.Y - fy0), e1 = (alongY ? q1.X - fx0 : q1.Y - fy0);
                            if (Math.Min(e0, e1) < -3 * Math.Min(bw, bh) || Math.Max(e0, e1) > 4 * Math.Min(bw, bh)) continue;
                            Console.WriteLine($"      line along it: {len / 25.4:0}\" long, offset {e0 / 25.4:0.0}\" to {e1 / 25.4:0.0}\" from the band's edge, w{o.LineWidth:0.00} #{o.Color.R:X2}{o.Color.G:X2}{o.Color.B:X2}");
                        }
                    }
                    if (n > 12) continue;
                    var edges = Enumerable.Range(0, n).Select(e => (A: p[e], B: p[(e + 1) % n], Len: Math.Sqrt(Math.Pow(p[(e + 1) % n].X - p[e].X, 2) + Math.Pow(p[(e + 1) % n].Y - p[e].Y, 2)))).OrderByDescending(e => e.Len).ToList();
                    var f1 = edges[0]; var f2 = edges[1];
                    if (f1.Len < 1219.2 || f2.Len < 1219.2 * 0.5) continue;
                    double ux = (f1.B.X - f1.A.X) / f1.Len, uy = (f1.B.Y - f1.A.Y) / f1.Len;
                    double vx = (f2.B.X - f2.A.X) / f2.Len, vy = (f2.B.Y - f2.A.Y) / f2.Len;
                    double angle = Math.Acos(Math.Min(1, Math.Abs(ux * vx + uy * vy))) * 180 / Math.PI;
                    if (angle > 5) continue;
                    double nx = -uy, ny = ux;
                    double d0 = Math.Abs((f2.A.X - f1.A.X) * nx + (f2.A.Y - f1.A.Y) * ny), d1 = Math.Abs((f2.B.X - f1.A.X) * nx + (f2.B.Y - f1.A.Y) * ny);
                    double tMin = Math.Min(d0, d1), tMax = Math.Max(d0, d1);
                    if (tMin < 101.6 || tMax > 1524) continue;
                    bool isWall = ovGeo.Walls.Any(w => Math.Abs(w.Outline.Average(q => q.X) - p.Average(q => q.X)) < 50 && Math.Abs(w.Outline.Average(q => q.Y) - p.Average(q => q.Y)) < 50);
                    if (isWall) continue;
                    double area = Math.Abs(PolygonProcessor.PolygonAreaMm2(p));
                    tapered++;
                    Console.WriteLine($"  filled shape of wall proportions not read as a wall: {n} pts, {f1.Len / 25.4:0} in long, {tMin / 25.4:0.0}-{tMax / 25.4:0.0} in thick, faces {angle:0.00} deg apart, fills {area / (f1.Len * tMax):0.00} of its box, colour #{r.Color.R:X2}{r.Color.G:X2}{r.Color.B:X2}, {(r.IsStroked ? "stroked" : "no stroke")}, at ({p.Average(q => q.X):0},{p.Average(q => q.Y):0}) mm");
                }
                if (tapered == 0) Console.WriteLine("  filled shapes of wall proportions not read as walls: none");
            }
            foreach (var w in ovGeo.Walls.OrderByDescending(w => Math.Sqrt(Math.Pow(w.End.X - w.Start.X, 2) + Math.Pow(w.End.Y - w.Start.Y, 2))))
            {
                double lenMm = Math.Sqrt(Math.Pow(w.End.X - w.Start.X, 2) + Math.Pow(w.End.Y - w.Start.Y, 2));
                Console.WriteLine($"  wall {lenMm / 25.4,7:0.0} x {w.ThicknessMm / 25.4,5:0.0} in   centre ({(w.Start.X + w.End.X) / 2,6:0},{(w.Start.Y + w.End.Y) / 2,6:0}) mm   outline pts {w.Outline.Count}");
                double wx0 = w.Outline.Min(p => p.X), wx1 = w.Outline.Max(p => p.X), wy0 = w.Outline.Min(p => p.Y), wy1 = w.Outline.Max(p => p.Y);
                int wallAt = ovRaw.FindIndex(r => r.Points.Count == 4 && r.IsFilled && Math.Abs(r.Points.Min(p => p.X) - wx0) < 1 && Math.Abs(r.Points.Max(p => p.X) - wx1) < 1
                                                  && Math.Abs(r.Points.Min(p => p.Y) - wy0) < 1 && Math.Abs(r.Points.Max(p => p.Y) - wy1) < 1);
                for (int i = 0; i < ovRaw.Count; i++)
                {
                    var r = ovRaw[i];
                    if (r.Points.Count < 3) continue;
                    double x0 = r.Points.Min(p => p.X), x1 = r.Points.Max(p => p.X), y0 = r.Points.Min(p => p.Y), y1 = r.Points.Max(p => p.Y);
                    double ox = Math.Min(x1, wx1) - Math.Max(x0, wx0), oy = Math.Min(y1, wy1) - Math.Max(y0, wy0);
                    if (ox <= 25 || oy <= 25) continue;
                    if ((x1 - x0) > 3 * (wx1 - wx0) && (y1 - y0) > 3 * (wy1 - wy0)) continue;   // a region, not a knockout
                    string when = i == wallAt ? "the wall itself" : i < wallAt ? "painted before" : "painted after";
                    Console.WriteLine($"      on it, {when} (path {i}): {(x1 - x0) / 25.4,6:0.0} x {(y1 - y0) / 25.4,5:0.0} in  colour #{r.Color.R:X2}{r.Color.G:X2}{r.Color.B:X2}  {(r.IsFilled ? "filled" : "unfilled")} {(r.IsStroked ? "stroked" : "unstroked")} {(r.IsClosed ? "closed" : "open")}  pts {r.Points.Count}{(r.IsAnnotation ? "  annotation" : "")}");
                }
            }
        }
        return 0;

        static double OvNum(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
