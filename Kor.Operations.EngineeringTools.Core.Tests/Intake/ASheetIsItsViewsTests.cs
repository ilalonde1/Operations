#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A sheet is its views (intake step 26). A tower sheet draws two plans side by side under two
/// underlined titles; written as one file both plans landed on every storey the sheet names. One
/// file per plan view, named sheet number, view index, view title, the way the office's export
/// names them; what is drawn belongs to the title nearest below it; a grid axis to every view it
/// crosses.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: two titled plans found from their words and the stroke under them, the
/// stroke running past the words (from the view's bubble) as it does; the columns, walls, slabs
/// and lines drawn above each going to it; the face-line and slab-edge indices surviving the
/// split; a vertical axis going to the view it crosses and a horizontal one to both; a sheet with
/// one title, and with none, staying one part; a heading that names no plan, or ends in a colon,
/// or sits in the title block, not being a view; a title drawn with a double stroke counted
/// once; the view file's name. WHAT IT DOES NOT: a real sheet (31168's S2.20.1 and S2.21.1 in
/// the five-set build); views stacked one above the other; what is drawn under no title at all,
/// which goes to the nearest title across; a title whose words the extractor split onto two
/// baselines.
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class ASheetIsItsViewsTests
{
    private const double W = 3024, H = 2160;
    private const double MmPerPt = 96 * 25.4 / 72;                    // a 1:96 sheet

    private static TT Tok(string text, double x, double y) => new(text, x + 20, y + 4, x, y, x + 40, y + 8);

    /// <summary>A stroke under a title: from x0 to x1 at y, drawn as one two-point path.</summary>
    private static GP Stroke(double x0, double x1, double y)
        => new([(x0, y), (x1, y)], false, false, true, x0, y, x1, y);

    private static PC Page(IEnumerable<(string Text, double X, double Y)> words, params GP[] paths)
        => new(1, W, H, words.Select(w => Tok(w.Text, w.X, w.Y)).ToList(), paths.ToList());

    /// <summary>Two plans side by side: LEVEL 3 at the left, LEVEL 4 at the right, titles at y 100, each underlined from its bubble at the left.</summary>
    private static PC TwoPlans() => Page(
        [("LEVEL", 400, 100), ("3", 440, 100), ("PLAN", 480, 100),
         ("LEVEL", 1800, 100), ("4", 1840, 100), ("PLAN", 1880, 100), ("(L4-L14)", 1920, 100)],
        Stroke(340, 525, 98), Stroke(1740, 1965, 98));

    private static ExtractedGeometry Geometry()
    {
        var g = new ExtractedGeometry();
        // columns and a wall in each plan, in millimetres (page point × 33.87)
        foreach (double x in new[] { 300.0, 500, 700 }) { g.Columns.Add((x * MmPerPt, 800 * MmPerPt)); g.ColumnColors.Add((0, 0, 0)); g.ColumnSizes.Add((400, 400)); g.ColumnIsAnnotation.Add(false); }
        foreach (double x in new[] { 1700.0, 1900 }) { g.Columns.Add((x * MmPerPt, 800 * MmPerPt)); g.ColumnColors.Add((0, 0, 0)); g.ColumnSizes.Add((400, 400)); g.ColumnIsAnnotation.Add(false); }
        // a filled wall in the left plan, a face wall in the right plan with its two face lines
        g.Walls.Add(new WallPanel([(300 * MmPerPt, 900 * MmPerPt), (700 * MmPerPt, 900 * MmPerPt), (700 * MmPerPt, 910 * MmPerPt), (300 * MmPerPt, 910 * MmPerPt)], (300 * MmPerPt, 905 * MmPerPt), (700 * MmPerPt, 905 * MmPerPt), 300));
        g.WallColors.Add((0, 0, 0)); g.WallIsAnnotation.Add(false);
        g.FirstFaceWall = 1;
        g.Lines.Add([(1700 * MmPerPt, 900 * MmPerPt), (1900 * MmPerPt, 900 * MmPerPt)]); g.LineColors.Add((0, 0, 0)); g.LineWidths.Add(0.5); g.LineIsAnnotation.Add(false);
        g.Lines.Add([(1700 * MmPerPt, 910 * MmPerPt), (1900 * MmPerPt, 910 * MmPerPt)]); g.LineColors.Add((0, 0, 0)); g.LineWidths.Add(0.5); g.LineIsAnnotation.Add(false);
        g.Walls.Add(new WallPanel([(1700 * MmPerPt, 900 * MmPerPt), (1900 * MmPerPt, 900 * MmPerPt), (1900 * MmPerPt, 910 * MmPerPt), (1700 * MmPerPt, 910 * MmPerPt)], (1700 * MmPerPt, 905 * MmPerPt), (1900 * MmPerPt, 905 * MmPerPt), 300));
        g.WallColors.Add((0, 0, 0)); g.WallIsAnnotation.Add(false);
        g.WallFaceLines[0] = 1; g.WallFaceLines[1] = 1;
        // a slab ring in the right plan made of four lines
        double x0 = 1650 * MmPerPt, x1 = 1950 * MmPerPt, y0 = 700 * MmPerPt, y1 = 1000 * MmPerPt;
        g.Slabs.Add([(x0, y0), (x1, y0), (x1, y1), (x0, y1)]); g.SlabColors.Add((0, 0, 0)); g.SlabIsAnnotation.Add(false);
        g.FirstEdgeSlab = 0;
        for (int i = 0; i < 4; i++) { g.Lines.Add([(x0, y0), (x1, y0)]); g.LineColors.Add((0, 0, 0)); g.LineWidths.Add(0.5); g.LineIsAnnotation.Add(false); g.SlabEdgeLines[2 + i] = 0; }
        // a vertical axis in each plan and one horizontal axis across the sheet
        g.GridAxes.Add(new GridAxis("1", true, 500 * MmPerPt));
        g.GridAxes.Add(new GridAxis("5", true, 1800 * MmPerPt));
        g.GridAxes.Add(new GridAxis("A", false, 850 * MmPerPt));
        return g;
    }

    private static readonly IReadOnlyDictionary<string, string> NoTitleBlock = new Dictionary<string, string>();

    [Fact]
    public void TwoTitledPlansAreTwoViewsLeftToRight()
    {
        var views = SheetViews.Titles(TwoPlans());
        Assert.Equal(2, views.Count);
        Assert.Equal("LEVEL 3 PLAN", views[0].Title);
        Assert.Equal("LEVEL 4 PLAN (L4-L14)", views[1].Title);
        Assert.Equal(340, views[0].MinXPts, 1);                                  // the stroke runs from the bubble
        Assert.Equal(98, views[0].YPts, 1);
    }

    [Fact]
    public void WhatIsDrawnAboveATitleIsThatViewsAndTheIndicesSurvive()
    {
        var parts = SheetViews.Split(Geometry(), SheetViews.Titles(TwoPlans()), MmPerPt, "S2.20.1", NoTitleBlock, "p22");
        Assert.Equal(2, parts.Count);
        var left = parts[0].Geometry; var right = parts[1].Geometry;
        Assert.Equal(3, left.Columns.Count);
        Assert.Equal(2, right.Columns.Count);
        Assert.Single(left.Walls); Assert.Equal(1, left.FirstFaceWall); Assert.Empty(left.WallFaceLines);
        Assert.Single(right.Walls); Assert.Equal(0, right.FirstFaceWall);
        Assert.Equal(new[] { 0, 0 }, right.WallFaceLines.Values.ToArray());       // both faces point at the one wall
        Assert.Single(right.Slabs); Assert.Equal(0, right.FirstEdgeSlab);
        Assert.Equal(4, right.SlabEdgeLines.Count);
        Assert.All(right.SlabEdgeLines.Values, v => Assert.Equal(0, v));
        Assert.Equal(6, right.Lines.Count);
        Assert.Equal(right.Lines.Count, right.LineWidths.Count);
        Assert.Empty(left.Slabs); Assert.Empty(left.Lines);
        Assert.Equal("S2.20.1_1_LEVEL 3 PLAN.dxf", parts[0].FileName);
        Assert.Equal("S2.20.1_2_LEVEL 4 PLAN (L4-L14).dxf", parts[1].FileName);
    }

    [Fact]
    public void AStoodDownAnchorStaysStoodDownInItsView()
    {
        // the fourth column (the first of the right-hand plan) was stood down as a tendon anchor before the split; the
        // view did not carry the flag and the exporter, seeing none, wrote the block as a column again (Codex audit
        // 2026-09-13, F2)
        var g = Geometry();
        g.ColumnIsTendonAnchor.AddRange([false, false, false, true, false]);
        var parts = SheetViews.Split(g, SheetViews.Titles(TwoPlans()), MmPerPt, "S2.20.1", NoTitleBlock, "p22");
        Assert.Equal([false, false, false], parts[0].Geometry.ColumnIsTendonAnchor);
        Assert.Equal([true, false], parts[1].Geometry.ColumnIsTendonAnchor);
    }

    /// <summary>
    /// A NOTE PRINTED IN A VIEW IS THAT VIEW'S (intake step 118, 2026-09-18). The slab callout leaves the page as a TEXT
    /// at its position, and the split carried none: on 31138's S2.26 the «8" SLAB» printed in each of the two plans
    /// reached neither, and both plates stayed the 12-in default while the one-plan sheets read theirs. WHAT THIS
    /// COVERS: a note goes to the view it stands in, by the same rule as a column. WHAT IT DOES NOT: the one-view
    /// sheet (the geometry passes through whole), a note between two plans (the nearest title below takes it).
    /// </summary>
    [Fact]
    public void ANotePrintedInAViewIsThatViews()
    {
        var g = Geometry();
        g.TextAnnotations.Add(new TextAnnotation("10\" SLAB", 500 * MmPerPt, 850 * MmPerPt));    // in the left plan
        g.TextAnnotations.Add(new TextAnnotation("8\" SLAB", 1800 * MmPerPt, 850 * MmPerPt));    // in the right plan
        var parts = SheetViews.Split(g, SheetViews.Titles(TwoPlans()), MmPerPt, "S2.20.1", NoTitleBlock, "p22");
        Assert.Equal("10\" SLAB", Assert.Single(parts[0].Geometry.TextAnnotations).Text);
        Assert.Equal("8\" SLAB", Assert.Single(parts[1].Geometry.TextAnnotations).Text);
    }

    [Fact]
    public void AnAxisGoesToEveryViewItCrosses()
    {
        var parts = SheetViews.Split(Geometry(), SheetViews.Titles(TwoPlans()), MmPerPt, "S2.20.1", NoTitleBlock, "p22");
        Assert.Equal(new[] { "1", "A" }, parts[0].Geometry.GridAxes.Select(a => a.Name));
        Assert.Equal(new[] { "5", "A" }, parts[1].Geometry.GridAxes.Select(a => a.Name));
    }

    [Fact]
    public void OneTitleOrNoneIsOneView()
    {
        var one = SheetViews.Titles(Page([("LEVEL", 400, 100), ("3", 440, 100), ("PLAN", 480, 100)], Stroke(380, 540, 98)));
        Assert.Single(one);
        var parts = SheetViews.Split(Geometry(), one, MmPerPt, "S2.20.1", new Dictionary<string, string> { ["SHEET TITLE"] = "LEVEL 3 PLAN" }, "p22");
        var part = Assert.Single(parts);
        Assert.Equal(5, part.Geometry.Columns.Count);
        Assert.Equal("S2.20.1_1_LEVEL 3 PLAN.dxf", part.FileName);
        Assert.Single(SheetViews.Split(Geometry(), Array.Empty<SheetViews.View>(), MmPerPt, "S2.20.1", NoTitleBlock, "p22"));
    }

    [Fact]
    public void AHeadingThatIsNotAPlansTitleIsNotAView()
    {
        // a notes heading naming the plan its notes are for, a key plan, the title block's own
        // title, and a double-stroked title: one view, counted once
        var page = Page(
            [("FOUNDATION", 400, 100), ("PLAN", 480, 100), ("REFERENCE", 530, 100), ("NOTES:", 600, 100),
             ("COLUMN", 400, 300), ("SCHEDULE", 460, 300), ("KEY", 530, 300), ("PLAN", 560, 300), ("-", 600, 300), ("LEVEL", 620, 300), ("2", 660, 300),
             ("LEVEL", 2600, 100), ("1", 2640, 100), ("PLAN", 2680, 100),
             ("LEVEL", 1800, 100), ("4", 1840, 100), ("PLAN", 1880, 100)],
            Stroke(380, 650, 98), Stroke(380, 710, 298), Stroke(2580, 2730, 98), Stroke(1740, 1930, 98), Stroke(1740, 1930, 97));
        var views = SheetViews.Titles(page);
        Assert.Equal("LEVEL 4 PLAN", Assert.Single(views).Title);
        Assert.False(SheetViews.NamesAPlan("COLUMN SCHEDULE KEY PLAN - LEVEL 2"));
        Assert.False(SheetViews.NamesAPlan("FOUNDATION PLAN REFERENCE NOTES:"));
        Assert.True(SheetViews.NamesAPlan("ROOF PLAN - CONCRETE OUTLINE"));
        Assert.True(SheetViews.NamesAPlan("LEVEL P2 PLAN - FOUNDATION PLAN"));
    }

    [Fact]
    public void PlansStackedOneAboveTheOtherSplitByTheDropToTheirTitles()
    {
        // 31168's tower C sheet: LEVEL 5–8 at the top, LEVEL 9 under it, the roof under that, the
        // three titles one above the other at nearly one x. Nearest across alone gave the top
        // plan's columns to whichever title's centre was a few points closer.
        var page = Page(
            [("LEVEL", 1400, 1455), ("5", 1440, 1455), ("PLAN", 1480, 1455),
             ("LEVEL", 1400, 608), ("9", 1440, 608), ("PLAN", 1480, 608),
             ("ROOF", 1500, 144), ("PLAN", 1540, 144)],
            Stroke(1360, 1736, 1453), Stroke(1360, 1693, 606), Stroke(1503, 1811, 142));
        var views = SheetViews.Titles(page);
        Assert.Equal(3, views.Count);
        var g = new ExtractedGeometry();
        foreach (double y in new[] { 1600.0, 1700, 1800 }) { g.Columns.Add((1300 * MmPerPt, y * MmPerPt)); g.ColumnColors.Add((0, 0, 0)); g.ColumnSizes.Add((400, 400)); g.ColumnIsAnnotation.Add(false); }
        foreach (double y in new[] { 800.0, 1000 }) { g.Columns.Add((1300 * MmPerPt, y * MmPerPt)); g.ColumnColors.Add((0, 0, 0)); g.ColumnSizes.Add((400, 400)); g.ColumnIsAnnotation.Add(false); }
        g.Columns.Add((1700 * MmPerPt, 400 * MmPerPt)); g.ColumnColors.Add((0, 0, 0)); g.ColumnSizes.Add((400, 400)); g.ColumnIsAnnotation.Add(false);
        var parts = SheetViews.Split(g, views, MmPerPt, "S2.41.1", NoTitleBlock, "p31");
        var byTitle = parts.ToDictionary(p => p.View!.Title, p => p.Geometry.Columns.Count);
        Assert.Equal(3, byTitle["LEVEL 5 PLAN"]);
        Assert.Equal(2, byTitle["LEVEL 9 PLAN"]);
        Assert.Equal(1, byTitle["ROOF PLAN"]);
    }

    [Fact]
    public void AViewWithNoBuildingInItsTitleIsTheSheetsBuildings()
    {
        var views = SheetViews.Titles(Page(
            [("LEVEL", 400, 100), ("35", 440, 100), ("PLAN", 480, 100),
             ("LEVEL", 1800, 100), ("33", 1840, 100), ("PLAN", 1880, 100), ("BLDG", 1920, 100), ("A", 1960, 100)],
            Stroke(380, 540, 98), Stroke(1780, 2010, 98)));
        var named = SheetViews.WithTheSheetsBuilding(views, "S2.22.1_1_BLDG A LEVEL 33 34, 35 PLAN AND ROOF PLAN.dxf");
        Assert.Equal("LEVEL 35 PLAN - BLDG A", named[0].Title);
        Assert.Equal("LEVEL 33 PLAN BLDG A", named[1].Title);                   // already tower A's
        Assert.Contains("A", PlanSheetNaming.Parse(SheetDxfName.ForView("S2.22.1", 1, named[0].Title, "p24")).BuildingTags);
        // an untagged sheet leaves its views as they are
        Assert.Equal("LEVEL 35 PLAN", SheetViews.WithTheSheetsBuilding(views, "S2.22.1_1_LEVEL 33 PLAN.dxf")[0].Title);
    }

    [Fact]
    public void ATitleWithNoStrokeUnderItIsNotAView()
    {
        var views = SheetViews.Titles(Page([("LEVEL", 400, 100), ("3", 440, 100), ("PLAN", 480, 100)]));
        Assert.Empty(views);
    }

    /// <summary>
    /// Step 60 (2026-09-13): 31089-01's eleven townhouse buildings draw two plans a sheet, titled "FOUNDATION
    /// PLAN" and "GROUND FLOOR SHOWING" over "MAIN FLOOR FRAMING OVER" - the underline under the second line,
    /// the word PLAN in neither. A title may run two lines, and a floor named by a word with a framing-over
    /// clause names a plan. WHAT THIS COVERS: the two-line title joined and read as the ground floor's; the
    /// single-line word-floor title; a note that mentions a floor and is underlined but names no plan.
    /// </summary>
    [Fact]
    public void ATitleMayRunTwoLinesAndAWordFloorWithItsFramingOverNamesAPlan()
    {
        // tokens are 8 high; the first line sits 12 above the underlined second line
        var page = Page(
            [("FOUNDATION", 400, 100), ("PLAN", 440, 100),
             ("GROUND", 1800, 112), ("FLOOR", 1840, 112), ("SHOWING", 1880, 112),
             ("MAIN", 1800, 100), ("FLOOR", 1840, 100), ("FRAMING", 1880, 100), ("OVER", 1920, 100),
             ("CONTINUOUS", 400, 600), ("TO", 440, 600), ("MAIN", 480, 600), ("FLOOR", 520, 600), ("SLAB", 560, 600)],
            Stroke(340, 490, 98), Stroke(1740, 1970, 98), Stroke(400, 600, 598));
        var views = SheetViews.Titles(page);
        Assert.Equal(2, views.Count);
        Assert.Equal("FOUNDATION PLAN", views[0].Title);
        Assert.Equal("GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER", views[1].Title);
        var ground = PlanSheetNaming.Parse("S2.01_2_GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER.dxf");
        Assert.Equal([1], ground.Levels);
        Assert.False(ground.IsRoof);
        Assert.True(SheetViews.NamesAPlan("UPPER FLOOR SHOWING ROOF FRAMING OVER"));
        Assert.True(SheetViews.NamesAPlan("BASEMENT FLOOR PLAN"));
        Assert.False(SheetViews.NamesAPlan("CONTINUOUS TO MAIN FLOOR SLAB"));
        Assert.False(SheetViews.NamesAPlan("FLOOR PLANS"));                     // a notes column's heading names no floor
    }

    /// <summary>
    /// The second audit's brief B (2026-09-14), findings 7, 8 and 10 on two-line titles. WHAT THIS COVERS: an
    /// underlined note under a title is not the title's second line (a second line begins with a floor, level,
    /// roof, plan or framing-over word); the join reaches two heights and not more; a first line with an underline
    /// of its own is consumed by the join, not a view of its own; a sole joined view on a sheet whose own name says
    /// no storey is written under the view's title. WHAT IT DOES NOT: a title of three lines.
    /// </summary>
    [Fact]
    public void ANoteUnderATitleIsNotItsSecondLineAndTheJoinReachesTwoHeights()
    {
        // LEVEL 3 PLAN, underlined; a note under it, underlined too (8 pt tokens, 12 pt apart)
        var noted = SheetViews.Titles(Page(
            [("LEVEL", 400, 112), ("3", 440, 112), ("PLAN", 480, 112),
             ("CONTINUOUS", 400, 100), ("TO", 440, 100), ("MAIN", 480, 100), ("FLOOR", 520, 100), ("SLAB", 560, 100)],
            Stroke(340, 540, 110), Stroke(340, 600, 98)));
        var one = Assert.Single(noted);
        Assert.Equal("LEVEL 3 PLAN", one.Title);
        Assert.Equal(110, one.YPts, 1);

        // the two lines of one title, each underlined (31089-01's way): one view, the first line consumed
        var twoLines = SheetViews.Titles(Page(
            [("GROUND", 1800, 112), ("FLOOR", 1840, 112), ("SHOWING", 1880, 112),
             ("MAIN", 1800, 100), ("FLOOR", 1840, 100), ("FRAMING", 1880, 100), ("OVER", 1920, 100)],
            Stroke(1740, 1970, 110), Stroke(1740, 1970, 98)));
        Assert.Equal("GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER", Assert.Single(twoLines).Title);

        // two heights apart joins (16 pt for 8 pt tokens); more does not
        var atTwo = SheetViews.Titles(Page(
            [("GROUND", 1800, 116), ("FLOOR", 1840, 116), ("SHOWING", 1880, 116), ("MAIN", 1800, 100), ("FLOOR", 1840, 100), ("FRAMING", 1880, 100), ("OVER", 1920, 100)],
            Stroke(1740, 1970, 98)));
        Assert.Equal("GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER", Assert.Single(atTwo).Title);
        var beyondTwo = SheetViews.Titles(Page(
            [("GROUND", 1800, 117), ("FLOOR", 1840, 117), ("SHOWING", 1880, 117), ("MAIN", 1800, 100), ("FLOOR", 1840, 100), ("FRAMING", 1880, 100), ("OVER", 1920, 100)],
            Stroke(1740, 1970, 98)));
        Assert.Empty(beyondTwo);

        // a sole joined view on a sheet that names no storey itself is written under its own title (B8)
        var parts = SheetViews.Split(Geometry(), twoLines, MmPerPt, null, NoTitleBlock, "job-p01");
        Assert.Equal("job-p01_1_GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER.dxf", Assert.Single(parts).FileName);
    }

    /// <summary>
    /// A TITLE THAT IS A STOREY'S NAME AND NOTHING ELSE IS THAT STOREY'S PLAN (step 90, WP6a item 5's class B): 31229's
    /// eight sheets "LEVEL P2" … "LEVEL 22 MECH" and 90101's "GROUND FLOOR", "PODIUM SECOND FLOOR" typed "other" for
    /// want of the word PLAN, and the sets built nothing. WHAT THIS COVERS: the storey-only titles type plan, with a
    /// bookmark's sheet-number prefix ignored; a title with any other word keeps its kind (an elevation, a detail, a
    /// schedule, a note); a title with no storey in it is not a plan. WHAT IT DOES NOT: what the plan's geometry holds
    /// (the composer still refuses a "plan" with no structure); a storey named in a language the vocabulary lacks.
    /// </summary>
    [Fact]
    public void ATitleThatIsAStoreysNameAloneIsThatStoreysPlan()
    {
        foreach (string t in new[] { "LEVEL P2", "LEVEL 1", "LEVEL 3 & 4", "LEVEL 6 - 21", "LEVEL 22 MECH", "GROUND FLOOR", "PODIUM SECOND FLOOR",
                                     "S-2.01  -  GROUND FLOOR", "PARKING P4", "LEVEL B3", "ROOF", "2ND FLOOR", "L12" })
        {
            Assert.True(SheetViews.NamesAStoreyAlone(t), t);
            Assert.Equal("plan", DrawingIntake.SheetTypeOf(t));
        }
        foreach (string t in new[] { "LEVEL 2 WALL ELEVATIONS", "LEVEL 2 - 5 TYPICAL DETAILS", "SHEAR WALL SCHEDULE LEVEL 3", "PODIUM MEZZANINE",
                                     "GENERAL NOTES", "SW1", "S-2.01", "MIXED USE DEVELOPMENT", "" })
            Assert.False(SheetViews.NamesAStoreyAlone(t), t);
        Assert.Equal("section/elevation", DrawingIntake.SheetTypeOf("LEVEL 2 WALL ELEVATIONS"));
        Assert.Equal("other", DrawingIntake.SheetTypeOf("PODIUM MEZZANINE"));
    }

    /// <summary>
    /// A TITLE'S UNDERLINE UNDERLINES EVERY LINE OF THE TITLE (intake step 125, 2026-09-18). 30838's S2.28 stacks two
    /// views of LEVEL 22: the upper titled "LEVEL 22 PLAN - CONCRETE OUTLINE" over "AND DIAPHRAGM REINFORCING" with the
    /// stroke under the second line only, the lower "LEVEL 22 PLAN - SLAB REINFORCING". One view was found, the sheet
    /// was one part, and the lower view's box stood as a 1,300 sq ft plate on every storey of the set. WHAT THIS
    /// COVERS: a line that names a plan, with no underline of its own, at the size and left edge of the underlined line
    /// under it, is that title's first line whatever the second line's words; the two views part by the drop; a title
    /// in larger type over an underlined note, with no stroke of its own, is neither joined nor a view. WHAT IT DOES
    /// NOT: a title of three lines; a second line that names a plan alone (that is its own title).
    /// </summary>
    [Fact]
    public void ATitlesUnderlineUnderlinesEveryLineOfTheTitle()
    {
        var views = SheetViews.Titles(Page(
            [("LEVEL", 1800, 712), ("22", 1840, 712), ("PLAN", 1880, 712), ("-", 1920, 712), ("CONCRETE", 1960, 712), ("OUTLINE", 2000, 712),
             ("AND", 1800, 700), ("DIAPHRAGM", 1840, 700), ("REINFORCING", 1880, 700),
             ("LEVEL", 1800, 100), ("22", 1840, 100), ("PLAN", 1880, 100), ("-", 1920, 100), ("SLAB", 1960, 100), ("REINFORCING", 2000, 100)],
            Stroke(1740, 2050, 698), Stroke(1740, 2050, 98)));
        Assert.Equal(2, views.Count);
        Assert.Contains(views, v => v.Title == "LEVEL 22 PLAN - CONCRETE OUTLINE AND DIAPHRAGM REINFORCING" && Math.Abs(v.YPts - 698) < 1);
        Assert.Contains(views, v => v.Title == "LEVEL 22 PLAN - SLAB REINFORCING" && Math.Abs(v.YPts - 98) < 1);
        // the two views part by the drop: what is drawn above the upper title is the upper view's
        var parts = SheetViews.Split(Geometry(), views, MmPerPt, "S2.28", NoTitleBlock, "job-p49");
        Assert.Equal(2, parts.Count);
        Assert.Contains(parts, p => p.FileName.Contains("CONCRETE OUTLINE AND DIAPHRAGM REINFORCING", StringComparison.Ordinal));
        Assert.Contains(parts, p => p.FileName.Contains("SLAB REINFORCING.dxf", StringComparison.Ordinal));

        // a title in larger type (12 pt over 8 pt tokens) with no stroke of its own, over an underlined note: not one title, not a view
        var noted = SheetViews.Titles(new PC(1, W, H,
            [new TT("LEVEL", 420, 118, 400, 112, 440, 124), new TT("3", 460, 118, 440, 112, 480, 124), new TT("PLAN", 500, 118, 480, 112, 520, 124),
             Tok("CONTINUOUS", 400, 100), Tok("TO", 440, 100), Tok("MAIN", 480, 100), Tok("FLOOR", 520, 100), Tok("SLAB", 560, 100)],
            [Stroke(340, 600, 98)]));
        Assert.Empty(noted);
    }
}
