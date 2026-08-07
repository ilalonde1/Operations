using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public class DxfPlanReaderTests
{
    private static string[] DxfWith(params string[] entityBlocks)
    {
        var lines = new List<string> { "0", "SECTION", "2", "ENTITIES" };
        foreach (string block in entityBlocks) lines.AddRange(block.Split('\n'));
        lines.AddRange(new[] { "0", "ENDSEC", "0", "EOF" });
        return lines.ToArray();
    }

    private static string Line(string layer, double x1, double y1, double x2, double y2)
        => $"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}";

    [Fact]
    public void ReadsLinesWithLayers()
    {
        var segs = DxfPlanReader.ReadSegments(DxfWith(Line("WALL", 0, 0, 10, 0), Line("COL", 1, 1, 2, 2)));

        Assert.Equal(2, segs.Count);
        Assert.Equal("WALL", segs[0].Layer);
        Assert.Equal(10, segs[0].Length, 6);
    }

    [Fact]
    public void IgnoresEntitiesOutsideTheEntitiesSection()
    {
        var lines = new List<string> { "0", "SECTION", "2", "HEADER" };
        lines.AddRange(Line("WALL", 0, 0, 10, 0).Split('\n'));
        lines.AddRange(new[] { "0", "ENDSEC", "0", "EOF" });

        Assert.Empty(DxfPlanReader.ReadSegments(lines));
    }

    [Fact]
    public void ReadsClosedLwPolylineAsFourSegments()
    {
        string poly = "0\nLWPOLYLINE\n8\nSLAB\n90\n4\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n10\n20\n10\n10\n0\n20\n10";
        var segs = DxfPlanReader.ReadSegments(DxfWith(poly));

        Assert.Equal(4, segs.Count);
        Assert.All(segs, s => Assert.Equal("SLAB", s.Layer));
    }

    [Fact]
    public void TessellatesArcsIntoChordsWithinTolerance()
    {
        var segs = DxfPlanReader.TessellateArc("W", 0, 0, 100, 0, 90).ToList();

        Assert.True(segs.Count >= 2);
        // Every vertex must sit on the circle.
        Assert.All(segs, s => Assert.Equal(100, Math.Sqrt(s.Start.X * s.Start.X + s.Start.Y * s.Start.Y), 3));
    }
}

public class PlanLoopBuilderTests
{
    private static DxfSegment Seg(double x1, double y1, double x2, double y2, string layer = "L")
        => new(layer, new DxfPoint(x1, y1), new DxfPoint(x2, y2));

    [Fact]
    public void StitchesScatteredSegmentsIntoOneClosedLoop()
    {
        // Deliberately out of order and with mixed directions, as Revit exports them.
        var segs = new[]
        {
            Seg(0, 0, 100, 0),
            Seg(100, 100, 0, 100),
            Seg(100, 0, 100, 100),
            Seg(0, 100, 0, 0),
        };

        var result = new PlanLoopBuilder().Build(segs);

        var loop = Assert.Single(result.Loops);
        Assert.True(loop.ClosedExactly);
        Assert.Equal(4, loop.Points.Count);
        Assert.Equal(10000, loop.Area, 3);
        Assert.Empty(result.OpenChains);
    }

    [Fact]
    public void BridgesASmallGapAndMarksTheLoopInexact()
    {
        var segs = new[]
        {
            Seg(0, 0, 100, 0),
            Seg(100, 0, 100, 100),
            Seg(100, 100, 0, 100),
            Seg(0, 100, 0, 2),   // stops 2 units short of the start
        };

        var result = new PlanLoopBuilder(joinTolerance: 0.05, bridgeTolerance: 6.0).Build(segs);

        var loop = Assert.Single(result.Loops);
        Assert.False(loop.ClosedExactly);
    }

    [Fact]
    public void ReportsAnOutlineThatCannotBeClosed()
    {
        var segs = new[]
        {
            Seg(0, 0, 100, 0),
            Seg(100, 0, 100, 100),   // open on two sides, gap far beyond the bridge tolerance
        };

        var result = new PlanLoopBuilder().Build(segs);

        Assert.Empty(result.Loops);
        Assert.Single(result.OpenChains);
    }
}

public class WallOutlineDecomposerTests
{
    private static PlanLoop Loop(params (double X, double Y)[] pts)
        => new("JBP_V-WALL", pts.Select(p => new DxfPoint(p.X, p.Y)).ToList(), closedExactly: true);

    [Fact]
    public void ASingleRectangleBecomesOneWallOnItsCentreline()
    {
        var walls = WallOutlineDecomposer.Decompose(
            Loop((0, 0), (240, 0), (240, 12), (0, 12)), new PlanClassificationOptions());

        var wall = Assert.Single(walls);
        Assert.Equal(12, wall.Thickness, 3);
        Assert.Equal(240, wall.Length, 3);
        Assert.Equal(6, wall.Start.Y, 3);   // centreline, not a face
        Assert.Equal(6, wall.End.Y, 3);
    }

    [Fact]
    public void AUShapedCoreBecomesThreeWalls()
    {
        // A core ribbon: 12" walls forming a U, traced as one closed outline.
        var walls = WallOutlineDecomposer.Decompose(
            Loop((0, 0), (120, 0), (120, 100), (108, 100), (108, 12), (12, 12), (12, 100), (0, 100)),
            new PlanClassificationOptions());

        Assert.Equal(3, walls.Count);
        Assert.All(walls, w => Assert.Equal(12, w.Thickness, 3));
    }

    [Fact]
    public void ARealCoreOutlineYieldsEveryWallInIt()
    {
        // Verbatim from 31168 B-LEVEL 30: a channel core — a 14" wall 398" long with a
        // 36" leg rising at each end. All three must come through.
        var walls = WallOutlineDecomposer.Decompose(
            Loop((1500, 3091), (1500, 3195), (1464, 3195), (1464, 3077),
                 (1862, 3077), (1862, 3195), (1826, 3195), (1826, 3091)),
            new PlanClassificationOptions());

        Assert.Equal(3, walls.Count);
        Assert.Single(walls, w => Math.Abs(w.Thickness - 14) < 0.5);
        Assert.Equal(2, walls.Count(w => Math.Abs(w.Thickness - 36) < 0.5));
    }

    [Fact]
    public void TheSameCoreAtItsRealCoordinatesYieldsEveryWall()
    {
        // Full precision, exactly as the DXF stores it. The right-hand leg's faces carry a
        // sub-picometre drift off vertical that rounded coordinates hide.
        var walls = WallOutlineDecomposer.Decompose(
            Loop((1500.050070113553, 3091.001012129015), (1500.050070113553, 3194.75101212908),
                 (1464.050070113553, 3194.75101212908), (1464.050070113553, 3077.001012129038),
                 (1862.050070113528, 3077.001012129034), (1862.050070113529, 3194.751012129078),
                 (1826.050070113529, 3194.751012129078), (1826.050070113528, 3091.001012128999)),
            new PlanClassificationOptions());

        Assert.Equal(3, walls.Count);
    }

    [Fact]
    public void CoreWallFacesLandOnTheirGridLines()
    {
        // 31168 B-LEVEL 30: the core straddles grids 15 and 16, which the ETABS model places
        // at x = 1500 and 1826. The drawing's faces sit at 1500.05 and 1826.05, so the export
        // already shares the model's coordinate system and must not be shifted onto it.
        var walls = WallOutlineDecomposer.Decompose(
            Loop((1500.05, 3091.001), (1500.05, 3194.751), (1464.05, 3194.751), (1464.05, 3077.001),
                 (1862.05, 3077.001), (1862.05, 3194.751), (1826.05, 3194.751), (1826.05, 3091.001)),
            new PlanClassificationOptions());

        var faces = walls.SelectMany(w => new[] { w.Start.X, w.End.X })
            .Concat(walls.Select(w => w.Start.X + w.Thickness / 2))
            .Concat(walls.Select(w => w.Start.X - w.Thickness / 2))
            .ToList();

        foreach (double grid in new[] { 1500.0, 1826.0 })
            Assert.Contains(faces, f => Math.Abs(f - grid) <= 1.0);
    }

    [Fact]
    public void FacesFurtherApartThanAWallCouldBeAreNotPaired()
    {
        var walls = WallOutlineDecomposer.Decompose(
            Loop((0, 0), (240, 0), (240, 200), (0, 200)),   // 200" apart — a room, not a wall
            new PlanClassificationOptions());

        Assert.Empty(walls);
    }
}

public class PlanSheetNamingTests
{
    [Theory]
    [InlineData("LEVEL 29 PLAN ( L29-35) - CONCRETE OUTLINE - BLDG B.dxf", "B", 29, 35)]
    [InlineData("LEVEL 5 PLAN (L5-L8) - CONCRETE OUTLINE - BLDG C.dxf", "C", 5, 8)]
    public void ExpandsALevelRangeFromTheSheetTitle(string file, string building, int first, int last)
    {
        var sheet = PlanSheetNaming.Parse(file);

        Assert.Equal(building, sheet.BuildingTag);
        Assert.Equal(last - first + 1, sheet.Levels.Count);
        Assert.Equal(first, sheet.Levels[0]);
        Assert.Equal(last, sheet.Levels[^1]);
    }

    [Fact]
    public void ReadsTheBuildingFromAStoreyPrefixedName()
    {
        var sheet = PlanSheetNaming.Parse("--Structural Plan - A-LEVEL 28.dxf");

        Assert.Equal("A", sheet.BuildingTag);
        Assert.Equal(new[] { 28 }, sheet.Levels);
    }

    [Fact]
    public void SheetNumbersAreNotMistakenForLevels()
    {
        var sheet = PlanSheetNaming.Parse("--Structural Plan - S2-32-1_2_LEVEL 29 PLAN - CONCRETE OUTLINE - BLDG B.dxf");

        Assert.Equal(new[] { 29 }, sheet.Levels);
    }

    [Fact]
    public void ATaggedSheetOnlyMatchesItsOwnBuilding()
    {
        var sheet = PlanSheetNaming.Parse("LEVEL 29 PLAN - CONCRETE OUTLINE - BLDG B.dxf");
        var stories = new[] { "A-LEVEL 29", "B-LEVEL 29", "LEVEL 29" };

        Assert.Equal(new[] { "B-LEVEL 29" }, PlanSheetNaming.MatchStories(sheet, stories));
    }

    [Fact]
    public void ParkadeSheetsMatchParkadeStoreysOnly()
    {
        var sheet = PlanSheetNaming.Parse("--Structural Plan - LEVEL P2 PLAN - CONCRETE OUTLINE.dxf");
        var stories = new[] { "LEVEL 2", "LEVEL P1", "LEVEL P2", "LEVEL P3" };

        Assert.Equal(new[] { 2 }, sheet.ParkadeLevels);
        Assert.Empty(sheet.Levels);
        Assert.Equal(new[] { "LEVEL P2" }, PlanSheetNaming.MatchStories(sheet, stories));
    }

    [Fact]
    public void AnUntaggedSheetPrefersTheUnprefixedStorey()
    {
        var sheet = PlanSheetNaming.Parse("--Structural Plan - LEVEL 10.dxf");
        var stories = new[] { "A-LEVEL 10", "B-LEVEL 10", "LEVEL 10" };

        Assert.Equal(new[] { "LEVEL 10" }, PlanSheetNaming.MatchStories(sheet, stories));
    }
}

public class E2kDocumentTests
{
    private static readonly string[] Reference =
    {
        "$ PROGRAM INFORMATION",
        "  PROGRAM  \"ETABS\"  VERSION \"21.2.0\"",
        "",
        "$ STORIES - IN SEQUENCE FROM TOP",
        "  STORY \"LEVEL 3\"  HEIGHT 120",
        "  STORY \"LEVEL 2\"  HEIGHT 144",
        "  STORY \"Base\"  HEIGHT 0",
        "",
        "$ MATERIAL PROPERTIES",
        "  MATERIAL  \"65 MPa Walls\"    TYPE \"Concrete\"    GRADE \"x\"",
        "",
        "$ POINT COORDINATES",
        "  POINT \"1\"  0 0 0",
        "",
        "$ AREA CONNECTIVITIES",
        "",
    };

    [Fact]
    public void AccumulatesStoreyElevationsFromTheBaseUpward()
    {
        var stories = E2kDocument.Parse(Reference).ReadStories();

        var level2 = stories.Single(s => s.Name == "LEVEL 2");
        var level3 = stories.Single(s => s.Name == "LEVEL 3");

        Assert.Equal(144, level2.Elevation, 3);
        Assert.Equal(0, level2.ElevationBelow, 3);
        Assert.Equal(264, level3.Elevation, 3);
        Assert.Equal(144, level3.ElevationBelow, 3);
    }

    [Fact]
    public void AppendKeepsSectionsAndTrailingBlankLines()
    {
        var doc = E2kDocument.Parse(Reference);
        doc.Append("POINT COORDINATES", new[] { "  POINT \"KP1\"  1 2 3" });

        var points = doc.LinesOf("POINT COORDINATES");
        Assert.Contains(points, l => l.Contains("KP1"));
        Assert.Equal(5, doc.SectionHeaders.Count());   // appending never adds or rewrites headers
    }

    [Fact]
    public void FindsAConcreteMaterialForGeneratedSections()
    {
        Assert.Equal("65 MPa Walls", E2kDocument.Parse(Reference).FindConcreteMaterial());
    }

    [Fact]
    public void ComposesWallsColumnsAndFloorsIntoTheDocument()
    {
        var doc = E2kDocument.Parse(Reference);
        var story = doc.ReadStories().Single(s => s.Name == "LEVEL 3");

        var geometry = new PlanGeometrySet();
        geometry.Walls.Add(new WallAxis(new DxfPoint(0, 0), new DxfPoint(120, 0), 12, "JBP_V-WALL"));
        geometry.Columns.Add(new ColumnFootprint(new DxfPoint(50, 50), 24, 24, "JBP_V_COL"));

        var summary = E2kGeometryComposer.Compose(
            doc, new[] { new StoryPlacement(story, geometry, "sheet.dxf") });

        Assert.Equal(1, summary.Walls);
        Assert.Equal(1, summary.Columns);

        var areas = doc.LinesOf("AREA CONNECTIVITIES");
        Assert.Contains(areas, l => l.Contains("PANEL") && l.Contains("KW1"));

        var lines = doc.LinesOf("LINE CONNECTIVITIES");
        Assert.Contains(lines, l => l.Contains("COLUMN") && l.Contains("KC1"));

        // The wall must span this storey: bottom edge at 144, top at 264.
        var points = doc.LinesOf("POINT COORDINATES");
        Assert.Contains(points, l => l.TrimEnd().EndsWith(" 144"));
        Assert.Contains(points, l => l.TrimEnd().EndsWith(" 264"));
    }
}
