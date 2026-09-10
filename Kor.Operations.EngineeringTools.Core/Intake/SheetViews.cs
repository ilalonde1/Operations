using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A SHEET IS ITS VIEWS (intake step 26).
///
/// A tower sheet draws two plans side by side — "LEVEL 3 PLAN" and "LEVEL 4 PLAN (L4-L14)" on
/// 31168's S2.20.1 — each under its own underlined title. Written as one DXF named for the sheet,
/// both plans' columns and walls landed on every storey the sheet's title names: 100 columns a
/// storey on L4–L14 where Revit's export, one view per file, gives 48. The office's own export
/// names a file per VIEW, sheet number, view index, view title, and the DXF side already reads a
/// view's storeys from that name; so the stick file's sheet is written the same way — one file
/// per plan view, each carrying what is drawn above its title.
///
/// A view's title is the underlined line of text the furniture reader already finds (a heading's
/// underline: a rule under a line of text, no wider than it), when it names a level, a parkade
/// level, a roof or a foundation. What is drawn belongs to the title nearest below it, by the
/// distance across; a grid axis to every view it crosses. A sheet with one title, or none, is one
/// view and is written as before.
/// </summary>
public static class SheetViews
{
    /// <summary>A plan view's title and where it is written, in page points.</summary>
    public sealed record View(string Title, double MinXPts, double MaxXPts, double YPts)
    {
        public double CentreXPts => (MinXPts + MaxXPts) / 2;
    }

    /// <summary>One view's share of the sheet's geometry, and the file it is written to.</summary>
    public sealed record Part(View? View, string FileName, ExtractedGeometry Geometry);

    /// <summary>
    /// The plan views the sheet titles, left to right: a line of text that names a plan, with a
    /// stroke drawn under it — the underline runs from the view's number bubble to the end of the
    /// words, so it is longer than the words and the furniture reader's heading underline (which
    /// asks for a rule no wider than its text) does not find it. A title in the title block's fifth
    /// of the width is the sheet's, not a view's; a heading that ends in a colon or names notes,
    /// a legend or a schedule is a list's heading, not a plan's title.
    /// </summary>
    public static IReadOnlyList<View> Titles(VectorPageReader.PageContent page)
    {
        ArgumentNullException.ThrowIfNull(page);

        // the horizontal strokes on the page: candidates for an underline
        var strokes = new List<(double X0, double X1, double Y)>();
        foreach (var p in page.Paths)
        {
            if (!p.IsStroked || p.IsFilled || p.IsAnnotation || p.Points.Count != 2) continue;
            var a = p.Points[0]; var b = p.Points[1];
            if (Math.Abs(b.Y - a.Y) > 1.0 || Math.Abs(b.X - a.X) < 20) continue;
            strokes.Add((Math.Min(a.X, b.X), Math.Max(a.X, b.X), (a.Y + b.Y) / 2));
        }

        var views = new List<View>();
        foreach (var line in TextLines(page))
        {
            if (line.CentreX / page.WidthPts >= TitleRegionMinFx) continue;
            if (!NamesAPlan(line.Text)) continue;
            // underlined: a stroke just under the line's bottom, covering at least half of its width
            var under = strokes
                .Where(s => s.Y <= line.MinY + 0.2 * line.Height && s.Y >= line.MinY - 1.2 * line.Height
                            && Math.Min(s.X1, line.MaxX) - Math.Max(s.X0, line.MinX) >= 0.5 * (line.MaxX - line.MinX))
                .OrderByDescending(s => s.Y)
                .FirstOrDefault();
            if (under.X1 <= under.X0) continue;
            var view = new View(line.Text, Math.Min(under.X0, line.MinX), Math.Max(under.X1, line.MaxX), under.Y);
            // the same title twice a point apart is one title drawn with a double stroke
            if (views.Any(v => v.Title == view.Title && Math.Abs(v.YPts - view.YPts) <= line.Height && Math.Abs(v.CentreXPts - view.CentreXPts) <= line.Height)) continue;
            views.Add(view);
        }
        return views.OrderBy(v => v.MinXPts).ToList();
    }

    private const double TitleRegionMinFx = 0.80;

    private readonly record struct TextLine(string Text, double MinX, double MaxX, double MinY, double Height)
    {
        public double CentreX => (MinX + MaxX) / 2;
    }

    /// <summary>The page's lines of text: tokens on one baseline, contiguous within two heights.</summary>
    private static IEnumerable<TextLine> TextLines(VectorPageReader.PageContent page)
    {
        foreach (var group in page.Words.OrderByDescending(t => t.Cy).GroupBy(t => Math.Round(t.Cy)))
        {
            var run = new List<VectorPageReader.TextToken>();
            foreach (var t in group.OrderBy(t => t.MinX))
            {
                if (run.Count > 0 && t.MinX - run[^1].MaxX > 2 * Math.Max(t.Height, run.Max(r => r.Height)))
                {
                    yield return Line(run);
                    run.Clear();
                }
                run.Add(t);
            }
            if (run.Count > 0) yield return Line(run);
        }

        static TextLine Line(List<VectorPageReader.TextToken> run) => new(
            string.Join(" ", run.Select(t => t.Text.Trim()).Where(s => s.Length > 0)),
            run.Min(t => t.MinX), run.Max(t => t.MaxX), run.Min(t => t.MinY), run.Max(t => t.Height));
    }

    /// <summary>A title that names what a plan is of: a level, a parkade level, a roof, a foundation — and says it is a plan.</summary>
    public static bool NamesAPlan(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || !title.Contains("PLAN", StringComparison.OrdinalIgnoreCase)) return false;
        // a key plan is a picture of where the sheet's plan sits, not a plan; a schedule's key plan names the level it keys
        if (title.Contains("KEY PLAN", StringComparison.OrdinalIgnoreCase)) return false;
        // a list's heading, not a plan's title: it ends in a colon or names the list
        string t = title.Trim();
        if (t.EndsWith(':') || t.Contains("NOTES", StringComparison.OrdinalIgnoreCase) || t.Contains("LEGEND", StringComparison.OrdinalIgnoreCase)
            || t.Contains("SCHEDULE", StringComparison.OrdinalIgnoreCase)) return false;
        var v = PlanSheetNaming.Vocabulary;
        return v.SingleLevel.IsMatch(title) || v.ParkadeLevel.IsMatch(title)
               || title.Contains("ROOF", StringComparison.OrdinalIgnoreCase)
               || title.Contains("FOUNDATION", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The sheet's geometry as its views' parts: one part per view when it titles two or more
    /// plans, else one part that is the sheet. <paramref name="mmPerPt"/> is what the geometry's
    /// millimetres are to the page's points (the scale denominator times a point in millimetres).
    /// </summary>
    public static IReadOnlyList<Part> Split(ExtractedGeometry geometry, IReadOnlyList<View> views, double mmPerPt,
        string? sheetNumber, IReadOnlyDictionary<string, string> titleBlock, string fallbackStem)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(views);
        if (views.Count < 2 || mmPerPt <= 0)
            return new[] { new Part(views.Count == 1 ? views[0] : null, SheetDxfName.For(sheetNumber, titleBlock, fallbackStem), geometry) };

        // which view each thing belongs to: the title nearest below it — by the drop to the title
        // plus how far the thing sits outside the title's own span across. Plans side by side share
        // a title height, so the span decides; plans stacked one above the other (31168's tower C
        // sheet: LEVEL 5–8, LEVEL 9 and the roof, one under the other) share a span, so the drop
        // decides. Failing a title below it, the nearest across.
        double Beyond(View v, double x) => x < v.MinXPts ? v.MinXPts - x : x > v.MaxXPts ? x - v.MaxXPts : 0;
        int Owner(double xMm, double yMm)
        {
            double x = xMm / mmPerPt, y = yMm / mmPerPt;
            int best = -1; double bestD = double.MaxValue;
            for (int k = 0; k < views.Count; k++)
            {
                if (views[k].YPts >= y) continue;
                double d = (y - views[k].YPts) + Beyond(views[k], x);
                if (d < bestD) { bestD = d; best = k; }
            }
            if (best >= 0) return best;
            for (int k = 0; k < views.Count; k++)
            {
                double d = Math.Abs(views[k].CentreXPts - x);
                if (d < bestD) { bestD = d; best = k; }
            }
            return best;
        }
        static (double X, double Y) Centroid(IReadOnlyList<(double X, double Y)> pts)
            => pts.Count == 0 ? (0, 0) : (pts.Average(p => p.X), pts.Average(p => p.Y));

        var slabOwner = geometry.Slabs.Select(s => Owner(Centroid(s).X, Centroid(s).Y)).ToList();
        var columnOwner = geometry.Columns.Select(c => Owner(c.X, c.Y)).ToList();
        var wallOwner = geometry.Walls.Select(w => Owner((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2)).ToList();
        var lineOwner = geometry.Lines.Select(l => Owner(Centroid(l).X, Centroid(l).Y)).ToList();
        var footingOwner = geometry.Footings.Select(f => Owner(Centroid(f.Outline).X, Centroid(f.Outline).Y)).ToList();
        var dropOwner = geometry.DropPanelCandidates.Select(d => Owner(Centroid(d).X, Centroid(d).Y)).ToList();

        // each view's extent, from what it owns, for the axes that cross it
        var extent = new (double MinX, double MinY, double MaxX, double MaxY, bool Any)[views.Count];
        for (int k = 0; k < views.Count; k++) extent[k] = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, false);
        void Grow(int k, double x, double y)
        {
            var e = extent[k];
            extent[k] = (Math.Min(e.MinX, x), Math.Min(e.MinY, y), Math.Max(e.MaxX, x), Math.Max(e.MaxY, y), true);
        }
        for (int i = 0; i < geometry.Slabs.Count; i++) foreach (var p in geometry.Slabs[i]) Grow(slabOwner[i], p.X, p.Y);
        for (int i = 0; i < geometry.Columns.Count; i++) Grow(columnOwner[i], geometry.Columns[i].X, geometry.Columns[i].Y);
        for (int i = 0; i < geometry.Walls.Count; i++) foreach (var p in geometry.Walls[i].Outline) Grow(wallOwner[i], p.X, p.Y);
        for (int i = 0; i < geometry.Lines.Count; i++) foreach (var p in geometry.Lines[i]) Grow(lineOwner[i], p.X, p.Y);

        var parts = new List<Part>();
        for (int k = 0; k < views.Count; k++)
        {
            var g = new ExtractedGeometry
            {
                PageWidthPts = geometry.PageWidthPts, PageHeightPts = geometry.PageHeightPts, ScaleDenominator = geometry.ScaleDenominator,
                PageCount = geometry.PageCount, RawPathCount = geometry.RawPathCount, IsVectorPdf = geometry.IsVectorPdf,
                WallRibbonsNotSplit = geometry.WallRibbonsNotSplit,
            };

            var slabIndex = new Dictionary<int, int>();
            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                if (slabOwner[i] != k) continue;
                slabIndex[i] = g.Slabs.Count;
                g.Slabs.Add(geometry.Slabs[i]);
                g.SlabColors.Add(i < geometry.SlabColors.Count ? geometry.SlabColors[i] : ((byte)0, (byte)0, (byte)0));
                g.SlabIsAnnotation.Add(i < geometry.SlabIsAnnotation.Count && geometry.SlabIsAnnotation[i]);
            }
            g.FirstEdgeSlab = Enumerable.Range(0, Math.Min(geometry.FirstEdgeSlab, geometry.Slabs.Count)).Count(i => slabOwner[i] == k);

            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                if (columnOwner[i] != k) continue;
                g.Columns.Add(geometry.Columns[i]);
                g.ColumnColors.Add(i < geometry.ColumnColors.Count ? geometry.ColumnColors[i] : ((byte)0, (byte)0, (byte)0));
                g.ColumnSizes.Add(i < geometry.ColumnSizes.Count ? geometry.ColumnSizes[i] : (0, 0));
                g.ColumnIsAnnotation.Add(i < geometry.ColumnIsAnnotation.Count && geometry.ColumnIsAnnotation[i]);
            }

            var wallIndex = new Dictionary<int, int>();
            for (int i = 0; i < geometry.Walls.Count; i++)
            {
                if (wallOwner[i] != k) continue;
                wallIndex[i] = g.Walls.Count;
                g.Walls.Add(geometry.Walls[i]);
                g.WallColors.Add(i < geometry.WallColors.Count ? geometry.WallColors[i] : ((byte)0, (byte)0, (byte)0));
                g.WallIsAnnotation.Add(i < geometry.WallIsAnnotation.Count && geometry.WallIsAnnotation[i]);
            }
            g.FirstFaceWall = Enumerable.Range(0, Math.Min(geometry.FirstFaceWall, geometry.Walls.Count)).Count(i => wallOwner[i] == k);
            foreach (var d in geometry.Doorways)
                if (wallIndex.TryGetValue(d.FirstPier, out int pier)) g.Doorways.Add(d with { FirstPier = pier });

            var lineIndex = new Dictionary<int, int>();
            for (int i = 0; i < geometry.Lines.Count; i++)
            {
                if (lineOwner[i] != k) continue;
                lineIndex[i] = g.Lines.Count;
                g.Lines.Add(geometry.Lines[i]);
                g.LineColors.Add(i < geometry.LineColors.Count ? geometry.LineColors[i] : ((byte)0, (byte)0, (byte)0));
                g.LineWidths.Add(i < geometry.LineWidths.Count ? geometry.LineWidths[i] : 0);
                g.LineIsAnnotation.Add(i < geometry.LineIsAnnotation.Count && geometry.LineIsAnnotation[i]);
                if (i < geometry.LineSectionHints.Count) g.LineSectionHints.Add(geometry.LineSectionHints[i]);
            }
            foreach (var (line, wall) in geometry.WallFaceLines)
                if (lineIndex.TryGetValue(line, out int l) && wallIndex.TryGetValue(wall, out int w)) g.WallFaceLines[l] = w;
            foreach (var (line, slab) in geometry.SlabEdgeLines)
                if (lineIndex.TryGetValue(line, out int l) && slabIndex.TryGetValue(slab, out int s)) g.SlabEdgeLines[l] = s;

            for (int i = 0; i < geometry.Footings.Count; i++) if (footingOwner[i] == k) g.Footings.Add(geometry.Footings[i]);
            for (int i = 0; i < geometry.DropPanelCandidates.Count; i++) if (dropOwner[i] == k) g.DropPanelCandidates.Add(geometry.DropPanelCandidates[i]);

            // an axis to every view it crosses; a match line to every view
            var e = extent[k];
            foreach (var a in geometry.GridAxes)
            {
                if (!e.Any) continue;
                bool crosses = a.Vertical ? a.AtMm >= e.MinX - AxisReachMm && a.AtMm <= e.MaxX + AxisReachMm
                                          : a.AtMm >= e.MinY - AxisReachMm && a.AtMm <= e.MaxY + AxisReachMm;
                if (crosses) g.GridAxes.Add(a);
            }
            g.MatchLines.AddRange(geometry.MatchLines);

            string file = SheetDxfName.ForView(sheetNumber, k + 1, views[k].Title, fallbackStem);
            parts.Add(new Part(views[k], file, g));
        }
        return parts;
    }

    /// <summary>A grid axis this far outside a view's drawn extent still crosses it: the bubble stands off the plan.</summary>
    private const double AxisReachMm = 3000;

    /// <summary>The sheet's parts, from the record: its views' titles read off its own page, and its geometry split among them.</summary>
    public static IReadOnlyList<Part> Parts(SheetRecord record, string fallbackStem)
    {
        ArgumentNullException.ThrowIfNull(record);
        var views = WithTheSheetsBuilding(Titles(record.Content), SheetDxfName.For(record, fallbackStem));
        double mmPerPt = record.ScaleDenominator.GetValueOrDefault() * PdfToSafeConstants.PointsToMm;
        return Split(record.Geometry, views, mmPerPt, record.SheetNumber, record.TitleBlock, fallbackStem);
    }

    /// <summary>
    /// A view titled for a level but not for a building is the sheet's building's: "LEVEL 35 PLAN"
    /// on a sheet titled "... BLDG A" is tower A's level 35, and its name says so, or it lands on no
    /// storey — the model names that storey A-L35.
    /// </summary>
    public static IReadOnlyList<View> WithTheSheetsBuilding(IReadOnlyList<View> views, string sheetName)
    {
        ArgumentNullException.ThrowIfNull(views);
        var tags = PlanSheetNaming.Parse(sheetName).BuildingTags;
        if (tags.Count != 1) return views;
        var v = PlanSheetNaming.Vocabulary;
        return views.Select(view => v.Building.IsMatch(view.Title) || v.PrefixBuilding.IsMatch(view.Title)
                ? view
                : view with { Title = $"{view.Title} - BLDG {tags[0]}" })
            .ToList();
    }
}
