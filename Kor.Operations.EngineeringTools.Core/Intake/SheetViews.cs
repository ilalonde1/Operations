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
        var lines = TextLines(page).ToList();
        (double X0, double X1, double Y) UnderlineOf(TextLine line)
        {
            // underlined: a stroke just under the line's bottom, covering at least half of its width, with no
            // other line of text between (a stroke below the NEXT line underlines that one)
            var under = strokes
                .Where(s => s.Y <= line.MinY + 0.2 * line.Height && s.Y >= line.MinY - 1.2 * line.Height
                            && Math.Min(s.X1, line.MaxX) - Math.Max(s.X0, line.MinX) >= 0.5 * (line.MaxX - line.MinX))
                .OrderByDescending(s => s.Y)
                .FirstOrDefault();
            if (under.X1 <= under.X0) return under;
            bool interposed = lines.Any(o => !o.Equals(line) && o.MinY < line.MinY - 0.3 * line.Height && o.MinY >= under.Y - 0.3 * o.Height
                    && Math.Min(o.MaxX, line.MaxX) - Math.Max(o.MinX, line.MinX) > 0);
            return interposed ? default : under;
        }
        // A TITLE MAY RUN TWO LINES (step 60, 2026-09-13): "GROUND FLOOR SHOWING" over "MAIN FLOOR FRAMING OVER",
        // each line underlined - 31089-01's eleven townhouse buildings, two plans a sheet, named no storey because
        // the second line names no plan and the first alone names the wrong thing. An underlined line that names
        // no plan by itself takes the line directly above it (within two heights, sharing its span) as its first
        // line when the two together name one; that first line is then no title of its own.
        var consumed = new HashSet<TextLine>();
        var joined = new Dictionary<TextLine, string>();
        foreach (var line in lines)
        {
            if (line.CentreX / page.WidthPts >= TitleRegionMinFx || NamesAPlan(line.Text)) continue;
            var under = UnderlineOf(line);
            if (under.X1 <= under.X0) continue;
            // a title's second line is made of title words - at least half its words are the vocabulary's, a plan
            // kind's, a number, a tag ("CONCRETE OUTLINE - NT", "CONCRETE OUTLINE BLDG B", "MAIN FLOOR FRAMING OVER");
            // an underlined note under a title is a sentence (the second audit's B7: "CONTINUOUS TO MAIN FLOOR SLAB"
            // under "LEVEL 3 PLAN" made one title of the two). The first cut asked the line to BEGIN with a title word
            // and lost 31065's and 31168's second lines, which begin with CONCRETE.
            // A SECOND LINE THAT BEGINS WITH A CONJUNCTION CONTINUES THE FIRST (step 109, 2026-09-16 21:40): 30838's S2.26
            // titles its concrete-outline plan "LEVEL 20 PLAN - CONCRETE OUTLINE" over "AND DIAPHRAGM REINFORCING", the
            // underline under the second line only, and none of that line's three words is a title word - so the page's
            // two plans of LEVEL 20 (the slab-reinforcing plan below it is underlined on one line) were one view, and
            // every wall and column of the storey stood twice, 30 m apart, on all 33 tower storeys (44 columns where her
            // model has 22; half of ours beyond her footprint; the frame registration spoiled). A line that opens with
            // AND, &, OR or WITH is the tail of the line above it whatever its other words: a note does not begin so.
            if (!ReadsLikeATitleLine(line.Text) && !BeginsWithAConjunction(line.Text)) continue;
            var above = lines.Where(a => a.MinY > line.MinY && a.MinY - line.MinY <= 2.0 * line.Height
                    && Math.Min(a.MaxX, line.MaxX) - Math.Max(a.MinX, line.MinX) >= 0.5 * Math.Min(a.MaxX - a.MinX, line.MaxX - line.MinX))
                .OrderBy(a => a.MinY).FirstOrDefault();
            if (above.Text is null || !NamesAPlan(above.Text + " " + line.Text)) continue;
            joined[line] = above.Text + " " + line.Text;
            consumed.Add(above);
        }
        foreach (var line in lines)
        {
            if (line.CentreX / page.WidthPts >= TitleRegionMinFx || consumed.Contains(line)) continue;
            string text = joined.TryGetValue(line, out var two) ? two : line.Text;
            if (!NamesAPlan(text)) continue;
            var under = UnderlineOf(line);
            if (under.X1 <= under.X0) continue;
            var view = new View(text, Math.Min(under.X0, line.MinX), Math.Max(under.X1, line.MaxX), under.Y);
            // the same title twice a point apart is one title drawn with a double stroke
            if (views.Any(v => v.Title == view.Title && Math.Abs(v.YPts - view.YPts) <= line.Height && Math.Abs(v.CentreXPts - view.CentreXPts) <= line.Height)) continue;
            views.Add(view);
        }
        return views.OrderBy(v => v.MinXPts).ToList();
    }

    private const double TitleRegionMinFx = 0.80;

    /// <summary>A line whose first word is AND, &amp;, OR or WITH: the tail of the line above it (step 109).</summary>
    public static bool BeginsWithAConjunction(string text)
    {
        string first = (text ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Equals("AND", StringComparison.OrdinalIgnoreCase) || first == "&"
               || first.Equals("OR", StringComparison.OrdinalIgnoreCase) || first.Equals("WITH", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One line of the page as the title reader saw it: the words, where, and what it made of them.</summary>
    public sealed record LineReading(string Text, double XPts, double YPts, bool InTitleBlock, bool Underlined, bool NamesPlan);

    /// <summary>
    /// The title reader's trace (step 109's instrument, `takeoff sheet-views`): every line of the page that names a
    /// plan and every underlined line (a title's second line is one), with whether it sits in the title block's
    /// fifth and whether a stroke underlines it - so "why is this page one view?" is answered by the reader, not by
    /// a guess. 30838's S2.26: "LEVEL 20 PLAN CONCRETE OUTLINE" no underline, "AND DIAPHRAGM REINFORCING" underlined.
    /// </summary>
    public static IReadOnlyList<LineReading> Explain(VectorPageReader.PageContent page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var strokes = new List<(double X0, double X1, double Y)>();
        foreach (var p in page.Paths)
        {
            if (!p.IsStroked || p.IsFilled || p.IsAnnotation || p.Points.Count != 2) continue;
            var a = p.Points[0]; var b = p.Points[1];
            if (Math.Abs(b.Y - a.Y) > 1.0 || Math.Abs(b.X - a.X) < 20) continue;
            strokes.Add((Math.Min(a.X, b.X), Math.Max(a.X, b.X), (a.Y + b.Y) / 2));
        }
        var result = new List<LineReading>();
        foreach (var line in TextLines(page))
        {
            bool plan = NamesAPlan(line.Text);
            bool underlined = strokes.Any(s => s.Y <= line.MinY + 0.2 * line.Height && s.Y >= line.MinY - 1.2 * line.Height
                                               && Math.Min(s.X1, line.MaxX) - Math.Max(s.X0, line.MinX) >= 0.5 * (line.MaxX - line.MinX));
            if (!plan && !underlined) continue;
            result.Add(new LineReading(line.Text, line.MinX, line.MinY, line.CentreX / page.WidthPts >= TitleRegionMinFx, underlined, plan));
        }
        return result.OrderByDescending(r => r.YPts).ToList();
    }

    /// <summary>
    /// A line made of title words: at least half of its words are the vocabulary's (a floor, level, parkade, roof,
    /// building, framing-over, basement or loft word, a floor noun), a plan kind's (CONCRETE OUTLINE,
    /// FOUNDATION PLAN - the compiled structural-plan words), PLAN, FRAMING, OVER, a number, punctuation, or a tag of
    /// one or two letters (NT, ST, A, B) that is not a word of a sentence (TO, OF, AT ...).
    /// </summary>
    private static bool ReadsLikeATitleLine(string text)
    {
        var v = PlanSheetNaming.Vocabulary;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PLAN", "PLANS", "FRAMING", "OVER" };
        foreach (string e in v.FloorWords) known.Add(e.Split('=')[0].Trim());
        foreach (string w in v.LevelWords.Concat(v.ParkadeWords).Concat(v.RoofWords).Concat(v.FramingOverWords).Concat(v.BasementWords).Concat(v.TopFloorWords).Concat(v.BuildingWords).Concat(v.FloorNouns)) known.Add(w);   // not the range words: TO is a sentence's word first
        foreach (string phrase in new PlanClassificationOptions().StructuralPlanWords) foreach (string w in phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)) known.Add(w);
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim('(', ')', ',', '-', ':', '.', '&')).Where(t => t.Length > 0).ToList();
        if (tokens.Count == 0) return false;
        // a tag is one or two letters that are not a word of a sentence (TO, OF, AT, IN, ON, BY, AS, OR, IS, IT, AN, A)
        var notTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TO", "OF", "AT", "IN", "ON", "BY", "AS", "OR", "IS", "IT", "AN", "BE", "DO", "NO", "UP", "SO", "IF", "WE", "US", "MY", "HE", "ME", "GO" };
        int titleWords = tokens.Count(t => known.Contains(t) || t.All(c => char.IsDigit(c) || c == ',' || c == '.' || c == '-') || (t.Length <= 2 && t.All(char.IsLetter) && !notTags.Contains(t)));
        return titleWords * 2 >= tokens.Count;
    }

    private readonly record struct TextLine(string Text, double MinX, double MaxX, double MinY, double Height)
    {
        public double CentreX => (MinX + MaxX) / 2;
    }

    /// <summary>The page's lines of text: tokens on one baseline (<see cref="TextBaselines"/>, step 81 - by closeness, not
    /// by rounding), contiguous within two heights.</summary>
    private static IEnumerable<TextLine> TextLines(VectorPageReader.PageContent page)
    {
        foreach (var run in TextBaselines.Lines(page.Words)) yield return Line(run);

        static TextLine Line(IReadOnlyList<VectorPageReader.TextToken> run) => new(
            string.Join(" ", run.Select(t => t.Text.Trim()).Where(s => s.Length > 0)),
            run.Min(t => t.MinX), run.Max(t => t.MaxX), run.Min(t => t.MinY), run.Max(t => t.Height));
    }

    /// <summary>
    /// A TITLE THAT IS A STOREY'S NAME AND NOTHING ELSE IS THAT STOREY'S PLAN (intake step 90, 2026-09-16; WP6a item 5's
    /// class B). 31229 (Quadra East, 2026) titles its eight sheets "LEVEL P2", "LEVEL 1", "LEVEL 3 &amp; 4", "LEVEL 6 - 21",
    /// "LEVEL 22 MECH"; 90101 "GROUND FLOOR", "PODIUM SECOND FLOOR" — no PLAN, no descriptor — and every one typed
    /// "other", so the sets built nothing: "no plan sheet with structure on it". A level word alone does not make a
    /// plan (31168's wall elevations and typical details name storeys in their titles), so the rule is stricter than a
    /// level word: EVERY word of the title is a storey word — the vocabulary's level, parkade, floor, range, basement,
    /// top-floor, roof and mezzanine words, a floor noun, an ordinal, a number, a level token (P2, L12, B4), "AND",
    /// "&amp;", "PODIUM", "PARKING", "MECH" — and at least one of them names a storey. "LEVEL 2 WALL ELEVATIONS" is not.
    /// </summary>
    public static bool NamesAStoreyAlone(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var voc = PlanSheetNaming.Vocabulary;
        var words = title.ToUpperInvariant().Split([' ', ',', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        // a bookmark writes the sheet's number before its title ("S-2.01 - GROUND FLOOR"): the number is not a word of the title
        while (words.Count > 0 && (words[0] == "-" || (words[0].IndexOfAny(['.', '-']) > 0 && SheetTitleReader.IsSheetNumberToken(words[0].Replace(" ", ""))))) words.RemoveAt(0);
        if (words.Count == 0) return false;
        bool namesOne = false;
        foreach (string w in words)
        {
            bool storeyToken = System.Text.RegularExpressions.Regex.IsMatch(w, @"^(?:[A-Z]{1,2})?\d{1,2}[A-Z]?$") && (char.IsDigit(w[0]) || voc.ParkadeWords.Concat(voc.LevelWords).Any(p => w.StartsWith(p, StringComparison.Ordinal)))
                || voc.LevelOfWord(w) is not null
                || voc.BasementWords.Contains(w, StringComparer.OrdinalIgnoreCase) || voc.TopFloorWords.Contains(w, StringComparer.OrdinalIgnoreCase)
                || voc.RoofWords.Contains(w, StringComparer.OrdinalIgnoreCase);
            bool joiningWord = voc.LevelWords.Contains(w, StringComparer.OrdinalIgnoreCase) || voc.ParkadeWords.Contains(w, StringComparer.OrdinalIgnoreCase)
                || voc.FloorNouns.Contains(w, StringComparer.OrdinalIgnoreCase) || voc.RangeWords.Contains(w, StringComparer.OrdinalIgnoreCase)
                || voc.MezzanineWords.Any(m => w.StartsWith(m, StringComparison.OrdinalIgnoreCase))
                || w is "&" or "AND" or "PODIUM" or "PARKING" or "PARKADE" or "MECH" or "MECHANICAL" or "-";
            if (storeyToken) namesOne = true;
            else if (!joiningWord) return false;
        }
        return namesOne;
    }

    /// <summary>
    /// A title that names what a plan is of: a level, a parkade level, a roof, a foundation, a floor named by
    /// a word (step 60: MAIN, GROUND, UPPER, a basement or a loft word) — and says it is a plan, or says what
    /// framing it shows over the floor ("GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER" is the ground floor's
    /// plan and never says PLAN).
    /// </summary>
    public static bool NamesAPlan(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var voc = PlanSheetNaming.Vocabulary;
        bool saysPlan = title.Contains("PLAN", StringComparison.OrdinalIgnoreCase);
        bool saysFramingOver = voc.OwnStoreyPart(title).Length < title.Length;
        if (!saysPlan && !saysFramingOver) return false;
        // a key plan is a picture of where the sheet's plan sits, not a plan; a schedule's key plan names the level it keys
        if (title.Contains("KEY PLAN", StringComparison.OrdinalIgnoreCase)) return false;
        // a list's heading, not a plan's title: it ends in a colon or names the list
        string t = title.Trim();
        if (t.EndsWith(':') || t.Contains("NOTES", StringComparison.OrdinalIgnoreCase) || t.Contains("LEGEND", StringComparison.OrdinalIgnoreCase)
            || t.Contains("SCHEDULE", StringComparison.OrdinalIgnoreCase)) return false;
        // what the plan is OF is said before its framing-over clause: "UPPER FLOOR SHOWING ROOF FRAMING OVER" is
        // the upper floor's, not the roof's, and "MAIN FLOOR SHOWING 2ND FLOOR FRAMING OVER" is the main floor's
        string own = voc.OwnStoreyPart(title);
        return voc.SingleLevel.IsMatch(own) || voc.ParkadeLevel.IsMatch(own)
               || own.Contains("ROOF", StringComparison.OrdinalIgnoreCase)
               || own.Contains("FOUNDATION", StringComparison.OrdinalIgnoreCase)
               || voc.WordFloor.IsMatch(own) || voc.Basement.IsMatch(own) || voc.TopFloor.IsMatch(own);
    }

    /// <summary>
    /// The sheet's geometry as its views' parts: one part per view when it titles two or more
    /// plans, else one part that is the sheet. <paramref name="mmPerPt"/> is what the geometry's
    /// millimetres are to the page's points (the scale denominator times a point in millimetres).
    /// </summary>
    public static IReadOnlyList<Part> Split(ExtractedGeometry geometry, IReadOnlyList<View> views, double mmPerPt,
        string? sheetNumber, IReadOnlyDictionary<string, string> titleBlock, string fallbackStem, string? sheetDxfName = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(views);
        if (views.Count < 2 || mmPerPt <= 0)
        {
            string name = sheetDxfName ?? SheetDxfName.For(sheetNumber, titleBlock, fallbackStem);
            // ONE view whose sheet name says no storey is written under the view's own title (the second audit's B8: a
            // sole "GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER" was written as "job-p01.dxf" and named no storey)
            if (views.Count == 1 && !PlanSheetNaming.Parse(name).HasPlacement && PlanSheetNaming.Parse(views[0].Title + ".dxf").HasPlacement)
                name = SheetDxfName.For(sheetNumber, titleBlock, fallbackStem, null, views[0].Title);
            return new[] { new Part(views.Count == 1 ? views[0] : null, name, geometry) };
        }

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
                g.ColumnIsTendonAnchor.Add(i < geometry.ColumnIsTendonAnchor.Count && geometry.ColumnIsTendonAnchor[i]);   // a stood-down anchor stays stood down in its view (Codex 2026-09-13, F2)
            }

            var wallIndex = new Dictionary<int, int>();
            for (int i = 0; i < geometry.Walls.Count; i++)
            {
                if (wallOwner[i] != k) continue;
                wallIndex[i] = g.Walls.Count;
                g.Walls.Add(geometry.Walls[i]);
                g.WallColors.Add(i < geometry.WallColors.Count ? geometry.WallColors[i] : ((byte)0, (byte)0, (byte)0));
                g.WallIsAnnotation.Add(i < geometry.WallIsAnnotation.Count && geometry.WallIsAnnotation[i]);
                g.WallTypeCodes.Add(i < geometry.WallTypeCodes.Count ? geometry.WallTypeCodes[i] : null);
                g.WallIsPartition.Add(i < geometry.WallIsPartition.Count && geometry.WallIsPartition[i]);
                g.WallIsDimensionString.Add(i < geometry.WallIsDimensionString.Count && geometry.WallIsDimensionString[i]);
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
            // THE SHEET TAGS ITS WALLS, NOT THE VIEW. The tagging decision (step 34: ten tags or more and
            // an untagged wall is no wall) was taken on the whole sheet at intake, and the composer takes
            // it again on each DXF it is handed by counting that DXF's tags — a sheet of two views with six
            // tags each had stood its untagged walls down and then counted as tagging nothing in the model
            // (Codex audit 2026-09-11, F13). Every view carries the sheet's every tag, so the two decisions
            // agree; a tag is a word with a position, not geometry, and one outside the view's frame
            // places nothing.
            g.WallTypeTags.AddRange(geometry.WallTypeTags);

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
        string sheetName = SheetDxfName.For(record, fallbackStem);
        var views = WithTheSheetsBuilding(Titles(record.Content), sheetName);
        double mmPerPt = record.ScaleDenominator.GetValueOrDefault() * PdfToSafeConstants.PointsToMm;
        return Split(record.Geometry, views, mmPerPt, record.SheetNumber, record.TitleBlock, fallbackStem, sheetName);
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
