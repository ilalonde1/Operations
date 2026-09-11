#nullable enable
using System.Globalization;
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
/// The counterexamples the Codex audit of steps 31–41 gave (docs/codex/CODEX-PDF-INTAKE-STEPS-31-41-
/// ADVERSARIAL-AUDIT-RESPONSE.md, 2026-09-11), each as the smallest input the response stated, each
/// now doing what the section claimed. Numbered as the response numbers them.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: F1 a range sheet keeping its wall on the storey the enlargement does not cover;
/// F2 two prefixed buildings both founded; F3 a finish layer not hiding a concrete core; F6 a
/// partition crossing a concrete wall leaving it; F7 a short earlier pier not taking a long later
/// wall, while a wall drawn 8 in longer on the second sheet (31138's stubs) IS the same wall and a
/// pier under the middle of a wall twice its length is not (the earlier must have drawn MORE THAN
/// HALF — the audit's own "both ends within reach" put 25 walls into four sets twice, 2026-09-11);
/// F9 a bare identifier inside a wall not flagging it, a bare number that agrees with the grid
/// flagging it; F10 a flagged dimension string passing no tag along a run; F11 two roof storeys and
/// two roof sheets, one each; F12 "ROOF LEVEL 3" read as level 3; F13 a sheet's tags carried whole
/// into every view; F14 a doorway leaving with its stripes; F16 a larger shape beside a run not
/// taken as its end cell; F17 a 300 → 400 taper refused; F18 a remark before the first layer kept;
/// F19 a decimal millimetre thickness read; F21 a wall turned about its centre seen by the diff's
/// key (in the script, not here). WHAT IT DOES NOT: F4 (three precast piers abutting within an inch
/// read as cells) and F5 (three equal walls at one pitch read as stripes), which are documented
/// limits, not fixed; F8 at the composer's admission gate, covered by the range-sheet Run below only
/// for a sheet with structure; F15 in the ledger, covered by `EveryPathHasExactlyOneFateTests`; the
/// instruments' arithmetic (F21–F24), which are scripts.
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]
public sealed class TheAuditsCounterexamplesForSteps31To41Tests
{
    private const double W = 3024, H = 2160;
    private static TT Tok(string text, double x, double y) => new(text, x, y, x - 10, y - 4, x + 10, y + 4);
    private static WallAxis Wall(double x0, double y0, double x1, double y1) => new(new DxfPoint(x0, y0), new DxfPoint(x1, y1), 8, "KOR_V-WALL");
    private static PlanLoop Box(string layer, double x0, double y0, double x1, double y1) =>
        new(layer, [new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1)], true);
    private static (PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>) Sheet(string name, IReadOnlyList<string> storeys, PlanGeometrySet g)
        => (PlanSheetNaming.Parse(name), g, storeys);
    private static PlanGeometrySet Tagging(PlanGeometrySet g)
    {
        for (int i = 0; i < WallTypeTagging.TaggingSheetMinTags; i++) g.Tags.Add(new DxfPositionedTag("S8.1", new DxfPoint(i * 10, 0), "KOR_WALLTYPE", "S8.1"));
        return g;
    }

    [Fact]
    public void F2_TwoBuildingsFoundedApartBothKeepTheirBase()
    {
        var storeys = new List<StoreyLadder.Storey>
        {
            new("A-L2", "A-L1", 3000, 0), new("A-L3", "A-L2", 3000, 0),
            new("B-L2", "B-L1", 3000, 0), new("B-L3", "B-L2", 3000, 0),
        };
        var chain = SetStoreys.Levels(SetStoreys.Reconcile([(1, storeys)], 1));
        Assert.Equal(["A-L1", "B-L1"], chain.Bases.OrderBy(b => b));
        Assert.Contains("B-L3", chain.Levels.Select(l => l.Name));
        Assert.Empty(chain.Unchained);
    }

    [Fact]
    public void F3_AFinishLayerAboveAConcreteCoreDoesNotMakeItAStudWall()
    {
        var m = AssemblySchedule.MaterialOf("EXTERIOR WALL", ["15.9mm GYPSUM BOARD", "200mm CONCRETE WALL"]);
        Assert.Equal(AssemblySchedule.Material.Concrete, m);
        Assert.Equal(200, AssemblySchedule.ThicknessOf("EXTERIOR WALL", ["15.9mm GYPSUM BOARD", "200mm CONCRETE WALL"], m));
    }

    [Fact]
    public void F6_APartitionCrossingAConcreteWallDoesNotTakeIt()
    {
        var a = new PlanGeometrySet(); a.Walls.Add(Wall(0, 0, 6000, 0));
        var b = new PlanGeometrySet(); b.Partitions.Add(Box("KOR_PARTITION", 2950, -2000, 3050, 2000));   // a stud wall across it
        b.Columns.Add(new ColumnFootprint(new DxfPoint(9000, 9000), 24, 24, "KOR_V_COL"));
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)>
        {
            Sheet("A104_1_LEVEL 3 PLAN.dxf", ["L3"], a), Sheet("A415_1_LEVEL 3 PLAN (SW).dxf", ["L3"], b),
        };
        DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 6);
        Assert.Single(a.Walls);                                                                // the concrete wall stands
        // and a partition RUNNING ALONG it, holding both its ends, still takes it
        b.Partitions.Clear(); b.Partitions.Add(Box("KOR_PARTITION", -10, -3, 6010, 3));
        DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 6);
        Assert.Empty(a.Walls);
    }

    [Fact]
    public void F7_AShortEarlierPierDoesNotTakeALongLaterWall()
    {
        var first = new PlanGeometrySet(); first.Walls.Add(Wall(2400, 0, 3600, 0));               // a 1.2 m pier
        var second = new PlanGeometrySet(); second.Walls.Add(Wall(0, 0, 6000, 0));                // the 6 m wall
        var parsed = new List<(PlanSheetInfo, PlanGeometrySet, IReadOnlyList<string>)>
        {
            Sheet("A104_1_LEVEL 3 PLAN.dxf", ["L3"], first), Sheet("A204_1_LEVEL 3 SLAB PLAN.dxf", ["L3"], second),
        };
        DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 6);
        Assert.Single(second.Walls);                                                             // the long wall stands
        // a later wall the earlier one COVERS is still the same wall drawn twice
        second.Walls.Clear(); second.Walls.Add(Wall(2410, 3, 3590, 3));
        DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 6);
        Assert.Empty(second.Walls);
        // and so is one drawn a little longer on the second sheet: 31138's stub walls are 1,219 on the 55'-0 plan
        // and 1,414 on the 64'-1 plan (in mm, so reach 152) - "both ends within reach" modelled them twice
        // (2026-09-11), the earlier wall having drawn 86% of the later
        first.Walls.Clear(); first.Walls.Add(Wall(0, -11649, 0, -10430));
        second.Walls.Clear(); second.Walls.Add(Wall(0, -11641, 0, -10226));
        DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 152);
        Assert.Empty(second.Walls);
        // a pier under the MIDDLE of a wall twice its length drew half of it - the wall is more of it, and stands
        first.Walls.Clear(); first.Walls.Add(Wall(600, 0, 1800, 0));
        second.Walls.Clear(); second.Walls.Add(Wall(0, 0, 2400, 0));
        DxfToEtabsService.StandDownToTaggedPartitions(parsed, reach: 6);
        Assert.Single(second.Walls);
    }

    [Fact]
    public void F9_ABareIdentifierInsideAWallIsNotALength()
    {
        var g = new ExtractedGeometry { ScaleDenominator = 96 };
        g.Walls.Add(new WallPanel([(0, -100), (6000, -100), (6000, 100), (0, 100)], (0, 0), (6000, 0), 200));
        // "500" with no grid span agreeing: a mark, not a length
        var bare = new DimensionStrings.Dimension(0, "500", 500, 3000, 0, false, true, null, null, null);
        Assert.Equal(0, DimensionStrings.StandDownWalls(g, [bare]));
        // "500" between two axes 500 apart: a length, and the wall is a dimension string
        var agreeing = new DimensionStrings.Dimension(0, "500", 500, 3000, 0, false, true, "1", "2", 500);
        Assert.Equal(1, DimensionStrings.StandDownWalls(g, [agreeing]));
        // a written length needs no span
        var written = new DimensionStrings.Dimension(0, "19'-8\"", 5994, 3000, 0, false, false, null, null, null);
        Assert.Equal(1, DimensionStrings.StandDownWalls(g, [written]));
    }

    [Fact]
    public void F10_AFlaggedDimensionStringPassesNoTagAlongARun()
    {
        var legend = new List<AssemblySchedule.Assembly>
        {
            new("WALL", "S8.1", "STEEL STUD PARTY WALL", ["STEEL STUDS"], new Dictionary<string, string>(), [], [], AssemblySchedule.Material.Stud, 41, 3),
        };
        var g = new ExtractedGeometry { ScaleDenominator = 96 };
        g.Walls.Add(new WallPanel([(0, -100), (2000, -100), (2000, 100), (0, 100)], (0, 0), (2000, 0), 200));      // the flagged one
        g.Walls.Add(new WallPanel([(2100, -100), (4000, -100), (4000, 100), (2100, 100)], (2100, 0), (4000, 0), 200)); // the real wall
        g.WallIsDimensionString.AddRange([true, false]);
        double Pt(double mm) => mm / (96 * PdfToSafeConstants.PointsToMm);
        var page = new PC(1, W, H, new List<TT> { new("S8.1", Pt(500), Pt(0), Pt(500) - 8, Pt(0) - 4, Pt(500) + 8, Pt(0) + 4) }, new List<GP>());
        WallTypeTagging.Apply(g, page, SheetFurniture.On(page, 0), legend);
        Assert.Equal([null, null], g.WallTypeCodes);                                              // the tag 1,600 mm from the real wall reaches it through nothing
        Assert.Equal([false, false], g.WallIsPartition);
    }

    [Fact]
    public void F11_TwoRoofStoreysAndTwoRoofSheetsTakeOneEach()
    {
        var stories = new[] { "ELEVATOR ROOF", "ROOF", "L6", "L5" };
        Assert.Equal(["ROOF"], PlanSheetNaming.MatchStories(PlanSheetNaming.Parse("S2.18.1_2_ROOF PLAN CONCRETE OUTLINE ST.dxf"), stories));
        Assert.Equal(["ELEVATOR ROOF"], PlanSheetNaming.MatchStories(PlanSheetNaming.Parse("S2.11.1_1_ELEVATOR ROOF PLAN.dxf"), stories));
    }

    [Fact]
    public void F12_ANameFollowedByANumberIsThatNumberedLevel()
    {
        var words = new List<TT>
        {
            Tok("LEVEL", 130, 700), Tok("1", 160, 700),
            Tok("LEVEL", 130, 800), Tok("2", 160, 800),
            Tok("ROOF", 100, 900), Tok("LEVEL", 130, 900), Tok("3", 160, 900),                    // ROOF LEVEL 3: level 3
        };
        var storeys = StoreyLadder.Read(new PC(1, W, H, words, new List<GP>()), "1/8\" = 1'-0\"", []);
        Assert.Contains(storeys, s => s.Level == "L3" && s.LevelBelow == "L2");
        Assert.DoesNotContain(storeys, s => s.Level == "ROOF LEVEL");
    }

    [Fact]
    public void F13_EveryViewCarriesTheSheetsEveryTag()
    {
        const double MmPerPt = 96 * PdfToSafeConstants.PointsToMm;
        var g = new ExtractedGeometry { ScaleDenominator = 96 };
        // two plans side by side on one sheet: a wall under each title, six tags beside each wall
        foreach (double x in new[] { 300.0, 1700.0 })
        {
            g.Walls.Add(new WallPanel([(x * MmPerPt, 900 * MmPerPt), ((x + 400) * MmPerPt, 900 * MmPerPt), ((x + 400) * MmPerPt, 910 * MmPerPt), (x * MmPerPt, 910 * MmPerPt)],
                                      (x * MmPerPt, 905 * MmPerPt), ((x + 400) * MmPerPt, 905 * MmPerPt), 300));
            g.WallColors.Add((0, 0, 0)); g.WallIsAnnotation.Add(false); g.WallTypeCodes.Add(null); g.WallIsPartition.Add(true); g.WallIsDimensionString.Add(false);
            for (int i = 0; i < 6; i++) g.WallTypeTags.Add(("S8.1", (x + 50 + i * 40) * MmPerPt, 930 * MmPerPt));
        }
        var views = new List<SheetViews.View> { new("LEVEL 2 PLAN (NW)", 200, 800, 600), new("LEVEL 2 PLAN (NE)", 1600, 2200, 600) };
        var parts = SheetViews.Split(g, views, MmPerPt, "A410", new Dictionary<string, string>(), "p44");
        Assert.Equal(2, parts.Count);
        Assert.All(parts, v => Assert.Equal(12, v.Geometry.WallTypeTags.Count));                  // the sheet's twelve, in both
        Assert.All(parts, v => Assert.Single(v.Geometry.Walls));                                   // the walls still split by view
    }

    [Fact]
    public void F14_ADoorwayLeavesWithItsStripes()
    {
        var fates = new List<PathFate>();
        var paths = new List<RawSubpath>();
        foreach (double y in new[] { 30000.0, 30600.0, 31200.0 })
        {
            paths.Add(FateFixture.Rect(5000, 300, 60000, y) with { Color = (0xE0, 0xE0, 0xE0) });
            paths.Add(FateFixture.Rect(800, 340, 61800, y - 20) with { Color = (0xF0, 0xF0, 0xF0) });   // a doorway mask across each
        }
        var g = FateFixture.Classify(paths, fates);
        Assert.Empty(g.Walls);
        Assert.Empty(g.Doorways);
        Assert.DoesNotContain(fates, f => f.Reason == PathReason.Doorway);
    }

    [Fact]
    public void F16_ALargerShapeBesideARunIsNotItsEndCell()
    {
        var fates = new List<PathFate>();
        var g = FateFixture.Classify(
            [FateFixture.Rect(600, 800, 60000, 30000) with { Color = (0, 0, 0) }, FateFixture.Rect(600, 800, 60600, 30000) with { Color = (0, 0, 0) },
             FateFixture.Rect(600, 800, 61200, 30000) with { Color = (0, 0, 0) }, FateFixture.Rect(600, 1000, 61800, 30000)],
            fates);
        var column = Assert.Single(g.Columns);
        Assert.InRange(column.X, 61800, 62400);
        Assert.Equal(3, g.PatternCells.Count);
    }

    [Fact]
    public void F17_AThirdMoreAtOneEndIsATaper()
    {
        var fates = new List<PathFate>();
        var g = FateFixture.Classify([new RawSubpath([(0, 0), (6000, 0), (6000, 400), (0, 300)], true, (0xD0, 0xD0, 0xD0), true, false, 0.5, false)], fates);
        Assert.Empty(g.Walls);
    }

    [Fact]
    public void F18_F19_ARemarkBeforeTheFirstLayerIsKeptAndADecimalMillimetreIsRead()
    {
        // a card laid out as the card tests lay one out (page-down y, turned to PdfPig's bottom-up): symbol,
        // heading, a REMARK line before the first layer, then the layer with a decimal millimetre thickness
        static TT Down(string text, double x, double yDown, double w) => new(text, x + w / 2, H - yDown, x, H - yDown - 4, x + w, H - yDown + 4);
        double x = 200, y = 300;
        var words = new List<TT>
        {
            Down("C12", x, y + 1, 20), Down("C12", x + 44, y, 30), Down("-", x + 78, y + 3, 8), Down("EXTERIOR WALL", x + 90, y, 200),
            Down("INSTALL AFTER SURVEY", x + 170, y + 27, 300),
            Down("-", x + 160, y + 42, 6), Down("203.2mm CONCRETE WALL", x + 170, y + 39, 300),
            Down("PROVIDED", x + 77, y + 230, 40), Down("REFERENCE CODE", x + 162, y + 230, 70),
            Down("F.R.R.", x + 3, y + 248, 30), Down("2HR", x + 85, y + 246, 20), Down("V.B.B.L. 2019 / DIV-B / TABLE D-2.1.1", x + 162, y + 247, 200),
        };
        var cards = AssemblySchedule.Read(new PC(1, W, H, words, new List<GP>()), "WALL ASSEMBLY SCHEDULE", 5);
        var card = Assert.Single(cards);
        Assert.Contains(card.Remarks, r => r.Contains("INSTALL AFTER SURVEY", StringComparison.Ordinal));
        Assert.Equal(AssemblySchedule.Material.Concrete, card.Material);
        Assert.Equal(203.2, card.ThicknessMm!.Value, 1);
    }

    /// <summary>F1: a sheet that serves two storeys keeps its wall on the storey the enlargement does not cover.</summary>
    [Fact]
    public void F1_ARangeSheetKeepsItsWallOnTheStoreyNoEnlargementCovers()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-range-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string Line(string layer, double x1, double y1, double x2, double y2) => $"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}";
            string Text(string layer, string value, double x, double y) => $"0\nTEXT\n8\n{layer}\n10\n{x.ToString(CultureInfo.InvariantCulture)}\n20\n{y.ToString(CultureInfo.InvariantCulture)}\n40\n300\n1\n{value}";
            string Poly(string layer, double x0, double y0, double x1, double y1)
                => $"0\nLWPOLYLINE\n8\n{layer}\n90\n4\n70\n1\n10\n{x0}\n20\n{y0}\n10\n{x1}\n20\n{y0}\n10\n{x1}\n20\n{y1}\n10\n{x0}\n20\n{y1}";
            string head = "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES";
            var grid = new List<string>();
            foreach (var (name, x) in new[] { ("1", 0.0), ("2", 6000.0), ("3", 12000.0) }) { grid.Add(Line("GRID", x, -1000, x, 9000)); grid.Add(Text("GRID", name, x, 9000)); }
            foreach (var (name, y) in new[] { ("A", 0.0), ("B", 8000.0) }) { grid.Add(Line("GRID", -1000, y, 13000, y)); grid.Add(Text("GRID", name, -1000, y)); }
            var columns = new List<string>();
            foreach (double x in new[] { 0.0, 6000.0, 12000.0 }) foreach (double y in new[] { 0.0, 8000.0 }) columns.Add(Poly("KOR_V_COL", x - 200, y - 200, x + 200, y + 200));
            // the range sheet: LEVEL 2-3, one wall (300 thick, 5 m) between two columns
            var range = new List<string> { head }; range.AddRange(grid); range.AddRange(columns);
            range.Add(Poly("KOR_V-WALL", 500, -150, 5500, 150)); range.Add("0\nENDSEC\n0\nEOF");
            File.WriteAllText(Path.Combine(root, "S2.10_1_LEVEL 2-3 PLAN CONCRETE OUTLINE.dxf"), string.Join("\n", range));
            // the L2 enlargement: tags its walls (ten tags) and draws that wall as a stud partition footprint
            var enl = new List<string> { head }; enl.AddRange(grid); enl.AddRange(columns);
            for (int i = 0; i < 10; i++) enl.Add(Text("KOR_WALLTYPE", "S8.1", 700 + i * 300, 2000));
            enl.Add(Poly("KOR_PARTITION", 490, -40, 5510, 40)); enl.Add("0\nENDSEC\n0\nEOF");
            File.WriteAllText(Path.Combine(root, "A410_1_LEVEL 2 PLAN (NW).dxf"), string.Join("\n", enl));
            File.WriteAllLines(Path.Combine(root, "levels.csv"), ["# unit: mm", "L1,0", "L2,3000", "L3,6000", "L4,9000"]);
            string output = Path.Combine(root, "out.e2k");
            DxfToEtabsService.Run(new DxfToEtabsRequest { DxfFolder = root, LevelsFile = Path.Combine(root, "levels.csv"), LevelsUnit = "mm", OutputE2k = output, DeriveRulesFromReference = false });
            var assigns = File.ReadAllLines(output).Where(l => l.TrimStart().StartsWith("AREAASSIGN", StringComparison.Ordinal) && (l.Contains("PIER", StringComparison.Ordinal) || l.Contains("SPANDREL", StringComparison.Ordinal))).ToList();
            Assert.Single(assigns);                                                                          // one wall in the model: the L3 copy
            // the wall rises to the storey above the sheet's: the range sheet's L2 copy rises to L3, its L3 copy to L4
            Assert.DoesNotContain(assigns, l => l.Contains("\"L3\"", StringComparison.Ordinal));      // stood down on L2 (its wall rose to L3)
            Assert.Contains(assigns, l => l.Contains("\"L4\"", StringComparison.Ordinal));            // kept on L3 (rising to L4), which no enlargement covers
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
