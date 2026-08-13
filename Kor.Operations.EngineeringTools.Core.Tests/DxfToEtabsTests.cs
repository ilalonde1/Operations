using System.Globalization;
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

    /// <summary>
    /// A mezzanine reads as the level it sits above, so "LEVEL 1 PLAN MEZZ" and "LEVEL 1 PLAN" both
    /// parse as level 1. On 31168 the mezzanine was also the only storey whose name starts with
    /// LEVEL, so the rule above handed it both sheets and the ground floor of both towers was left
    /// empty — 45 walls and 67 columns modelled one storey up from where they belong.
    /// </summary>
    [Fact]
    public void AMezzanineSheetAndTheFloorBelowItDoNotShareAStorey()
    {
        var stories = new[] { "LEVEL 2", "LEVEL 1 MEZZ", "B-LEVEL 1", "A-LEVEL 1" };

        var ground = PlanSheetNaming.Parse("--Structural Plan - LEVEL 1 PLAN - CONCRETE OUTLINE.dxf");
        var mezz = PlanSheetNaming.Parse("--Structural Plan - LEVEL 1 PLAN MEZZ - CONCRETE OUTLINE.dxf");

        Assert.False(ground.IsMezzanine);
        Assert.True(mezz.IsMezzanine);

        // The ground floor sheet builds both towers' level 1, and never the mezzanine.
        Assert.Equal(new[] { "B-LEVEL 1", "A-LEVEL 1" }, PlanSheetNaming.MatchStories(ground, stories));

        // The mezzanine sheet builds the mezzanine, and nothing else.
        Assert.Equal(new[] { "LEVEL 1 MEZZ" }, PlanSheetNaming.MatchStories(mezz, stories));
    }

    /// <summary>
    /// Outlines are stitched across layers that are VARIANTS of one name — Revit splits a slab
    /// boundary over JBP_C_SLABEDG, -1 and -2 — but never across different names.
    ///
    /// Pooling everything that shared a role let a broken outline on one layer capture the segments
    /// of a clean one on another. On 31168's LEVEL 27 tower B sheet, JBP_V-WALL's six closed loops
    /// were welded to JBP_B_WALL-1's twelve open chains and the core came out as a single 326x173
    /// rectangle that resolves to nothing: one wall written where the drawing holds ten, which the
    /// engineer reported as "L27 tower B still has no walls".
    /// </summary>
    [Fact]
    public void OutlinesOnDifferentLayerFamiliesAreNotWeldedTogether()
    {
        static DxfSegment Seg(string layer, double x1, double y1, double x2, double y2)
            => new(layer, new DxfPoint(x1, y1), new DxfPoint(x2, y2));

        // A clean 240x12 wall outline on one layer.
        var clean = new[]
        {
            Seg("JBP_V-WALL", 0, 0, 240, 0),
            Seg("JBP_V-WALL", 240, 0, 240, 12),
            Seg("JBP_V-WALL", 240, 12, 0, 12),
            Seg("JBP_V-WALL", 0, 12, 0, 0),
        };

        // Broken stubs on a DIFFERENT wall layer, close enough to be bridged if pooled.
        var broken = new[]
        {
            Seg("JBP_B_WALL-1", 0, 6, 0, 100),
            Seg("JBP_B_WALL-1", 240, 6, 240, 100),
        };

        var result = StructuralPlanClassifier.Classify(clean.Concat(broken).ToList(), new PlanClassificationOptions());

        // The clean outline still becomes its wall, on its own centreline.
        var wall = Assert.Single(result.Walls, w => Math.Abs(w.Thickness - 12) < 0.5);
        Assert.Equal(240, wall.Length, 1);
        Assert.Equal(6, wall.Start.Y, 1);

        // Variants of ONE name still stitch: the same outline split across -1 and -2 must close.
        var split = new[]
        {
            Seg("JBP_C_SLABEDG", 0, 0, 600, 0),
            Seg("JBP_C_SLABEDG-1", 600, 0, 600, 600),
            Seg("JBP_C_SLABEDG-2", 600, 600, 0, 600),
            Seg("JBP_C_SLABEDG-2", 0, 600, 0, 0),
        };
        var slabs = StructuralPlanClassifier.Classify(split, new PlanClassificationOptions());
        Assert.Single(slabs.Slabs);
    }

    /// <summary>
    /// Numbers measured once on one engineer's model became constants. They are now read off the
    /// reference in front of us — but deriving is not automatically better than a constant, and the
    /// gate matters more than the derivation: 31168's reference implies a 37" "opening", because its
    /// partial-height panels are not door spandrels at all. A value is taken only when there is
    /// enough of it and it lands where the quantity can physically be.
    /// </summary>
    [Fact]
    public void ARuleIsTakenFromTheReferenceOnlyWhenTheAnswerIsCredible()
    {
        static E2kDocument Model(params (string Panel, double Depth, double StoreyHeight)[] spandrels)
        {
            var lines = new List<string> { "$ STORIES - IN SEQUENCE FROM TOP" };
            for (int i = 0; i < spandrels.Length; i++)
                lines.Add($"  STORY \"S{i}\"  HEIGHT {spandrels[i].StoreyHeight.ToString(CultureInfo.InvariantCulture)} ");
            lines.Add("  STORY \"Base\"  ELEV 0 ");

            lines.Add("$ POINT COORDINATES");
            for (int i = 0; i < spandrels.Length; i++)
            {
                lines.Add($"  POINT \"P{i}a\"  0 0 {spandrels[i].Depth.ToString(CultureInfo.InvariantCulture)}");
                lines.Add($"  POINT \"P{i}b\"  100 0");
            }

            lines.Add("$ AREA CONNECTIVITIES");
            for (int i = 0; i < spandrels.Length; i++)
                lines.Add($"  AREA \"{spandrels[i].Panel}\"  PANEL  4  \"P{i}a\"  \"P{i}b\"  \"P{i}b\"  \"P{i}a\"  0  0  0  0");

            lines.Add("$ AREA ASSIGNS");
            for (int i = 0; i < spandrels.Length; i++)
                lines.Add($"  AREAASSIGN  \"{spandrels[i].Panel}\"  \"S{i}\"  SECTION \"W12\"");

            return E2kDocument.Parse(lines);
        }

        // Ten spandrels 30" deep in 118" storeys: an 88" opening, which is a door.
        var credible = Model(Enumerable.Range(0, 10).Select(i => ($"W{i}", 30.0, 118.0)).ToArray());
        var taken = ReferenceRules.OpeningHeight(credible, fallback: 84.0);
        Assert.True(taken.FromReference);
        Assert.Equal(88.0, taken.Value);

        // Ten panels implying a 37" "opening" — not a door, so the standing value must survive.
        var absurd = Model(Enumerable.Range(0, 10).Select(i => ($"W{i}", 81.0, 118.0)).ToArray());
        var refused = ReferenceRules.OpeningHeight(absurd, fallback: 88.0);
        Assert.False(refused.FromReference);
        Assert.Equal(88.0, refused.Value);
        Assert.Contains("not the height of a door", refused.Because);

        // Too few to read anything from.
        var thin = Model(("W0", 30.0, 118.0), ("W1", 30.0, 118.0));
        Assert.False(ReferenceRules.OpeningHeight(thin, fallback: 88.0).FromReference);
    }

    /// <summary>
    /// The tool decides what linework IS from the layer it sits on, and the patterns are KOR's own
    /// drafting convention. A drafter who names columns anything else gets no error today: the
    /// columns are never seen, the model is built without them, and every count agrees with itself
    /// because nothing was read. A role with nothing, while unclaimed layers carry real geometry,
    /// is a naming mismatch and has to stop the run.
    /// </summary>
    [Fact]
    public void ARoleWithNoLayerIsAMismatchWhenGeometrySitsUnclaimed()
    {
        static DxfSegment On(string layer) => new(layer, new DxfPoint(0, 0), new DxfPoint(10, 0));
        var options = new PlanClassificationOptions();

        // Columns drawn on a layer this job has never seen, plus the usual walls and slab edges.
        var sheet = Enumerable.Repeat(On("JBP_V-WALL"), 300)
            .Concat(Enumerable.Repeat(On("JBP_C_SLABEDG"), 300))
            .Concat(Enumerable.Repeat(On("S-COLS-NEW"), 400))
            .ToList();

        var ledger = LayerLedger.Build(new[] { (IReadOnlyList<DxfSegment>)sheet }, options);

        Assert.Equal("walls", ledger.Single(e => e.Layer == "JBP_V-WALL").Role);
        Assert.Equal("slab edges", ledger.Single(e => e.Layer == "JBP_C_SLABEDG").Role);
        Assert.Null(ledger.Single(e => e.Layer == "S-COLS-NEW").Role);

        Assert.Equal(new[] { "columns" }, LayerLedger.RolesMissingWithGeometryUnclaimed(ledger));

        // Told the job's own layer name, the same drawings resolve and nothing is missing.
        var told = options with { ColumnLayerPatterns = new[] { "-COLS" } };
        var resolved = LayerLedger.Build(new[] { (IReadOnlyList<DxfSegment>)sheet }, told);
        Assert.Equal("columns", resolved.Single(e => e.Layer == "S-COLS-NEW").Role);
        Assert.Empty(LayerLedger.RolesMissingWithGeometryUnclaimed(resolved));
    }

    /// <summary>
    /// A drawing set really can have no columns. Refusing that would be a different kind of wrong,
    /// so a missing role only counts when unclaimed geometry is there to explain it.
    /// </summary>
    [Fact]
    public void ARoleWithNoLayerAndNothingUnclaimedIsAcceptedInSilence()
    {
        static DxfSegment On(string layer) => new(layer, new DxfPoint(0, 0), new DxfPoint(10, 0));

        var sheet = Enumerable.Repeat(On("JBP_V-WALL"), 300)
            .Concat(Enumerable.Repeat(On("JBP_C_SLABEDG"), 300))
            .ToList();

        var ledger = LayerLedger.Build(new[] { (IReadOnlyList<DxfSegment>)sheet }, new PlanClassificationOptions());
        Assert.Empty(LayerLedger.RolesMissingWithGeometryUnclaimed(ledger));
    }

    /// <summary>
    /// Units are read, not assumed. Every rule in the tool is a real length and every coordinate is
    /// written in the model's unit, so a drawing in millimetres does not fail — it produces a
    /// building of entirely the wrong size and says nothing. All 90 sheets across the two jobs
    /// built so far declare inches, which is exactly why this went unread.
    /// </summary>
    [Fact]
    public void DrawingUnitsAreReadFromTheDxfRatherThanAssumed()
    {
        static string[] WithUnits(string insunits) => new[]
        {
            "0", "SECTION", "2", "HEADER",
            "9", "$INSUNITS", "70", insunits,
            "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES", "0", "ENDSEC", "0", "EOF",
        };

        Assert.Equal(1.0, DxfPlanReader.UnitInInches(WithUnits("1")));      // inches
        Assert.Equal(12.0, DxfPlanReader.UnitInInches(WithUnits("2")));     // feet
        Assert.Equal(1.0 / 25.4, DxfPlanReader.UnitInInches(WithUnits("4")) ?? 0, 9);   // millimetres
        Assert.Equal(1000.0 / 25.4, DxfPlanReader.UnitInInches(WithUnits("6")) ?? 0, 9);// metres

        // Unitless is not a length. It must be null so the caller can refuse to guess.
        Assert.Null(DxfPlanReader.UnitInInches(WithUnits("0")));

        // A drawing that never says is likewise unknowable.
        Assert.Null(DxfPlanReader.UnitInInches(new[] { "0", "SECTION", "2", "ENTITIES", "0", "ENDSEC", "0", "EOF" }));
    }

    /// <summary>
    /// A rule is a real length, so it has to survive being restated in another unit: 48 inches is
    /// 1219.2 millimetres, not 48 millimetres. Ratios are dimensionless and must not move.
    /// </summary>
    [Fact]
    public void RulesRestatedInAnotherUnitKeepTheirRealSize()
    {
        var inches = new PlanClassificationOptions();
        var millimetres = inches.InUnitOf(1.0 / 25.4);

        Assert.Equal(inches.MinWallLength * 25.4, millimetres.MinWallLength, 6);
        Assert.Equal(inches.MinPanelOverlap * 25.4, millimetres.MinPanelOverlap, 6);

        // Areas go by the square.
        Assert.Equal(inches.MinPlateArea * 25.4 * 25.4, millimetres.MinPlateArea, 3);

        // Dimensionless rules are untouched.
        Assert.Equal(inches.MaxColumnAspect, millimetres.MaxColumnAspect);
        Assert.Equal(inches.PierFillRatio, millimetres.PierFillRatio);

        var compose = new ComposeOptions().InUnitOf(1.0 / 25.4);
        Assert.Equal(new ComposeOptions().OpeningHeight * 25.4, compose.OpeningHeight, 6);
    }

    /// <summary>
    /// A sheet title may LIST its levels instead of ranging them. Three 31138 sheets are titled
    /// "LEVEL 8, 9", "LEVEL 11, 12" and "LEVEL 17, 18"; only the first number was ever read, so
    /// L09, L12 and L18 were built empty — and nothing said so, because each sheet reported itself
    /// as placed on the one storey it did find.
    /// </summary>
    [Fact]
    public void ASheetTitleMayListItsLevelsRatherThanRangeThem()
    {
        var listed = PlanSheetNaming.Parse("Str-Structural Plan - DXF-S2-26_2_LEVEL 8, 9 PLAN CONCRETE OUTLINE Copy 1.dxf");
        Assert.Equal(new[] { 8, 9 }, listed.Levels);

        var ampersand = PlanSheetNaming.Parse("LEVEL 11 & 12 PLAN.dxf");
        Assert.Equal(new[] { 11, 12 }, ampersand.Levels);

        // A range still ranges, and a single level is still single.
        Assert.Equal(new[] { 4, 5, 6 }, PlanSheetNaming.Parse("LEVEL 4 PLAN (L4-L6).dxf").Levels);
        Assert.Equal(new[] { 20 }, PlanSheetNaming.Parse("--Structural Plan - LEVEL 20.dxf").Levels);

        // Both storeys are then filled, not just the first.
        var stories = new[] { "L07", "L08", "L09", "L10" };
        Assert.Equal(new[] { "L08", "L09" }, PlanSheetNaming.MatchStories(listed, stories));
    }

    /// <summary>
    /// 31138 names its mezzanine storey just "Mezz", with no level number, so a mezzanine sheet
    /// matches nothing by number. Its part plan — 11 walls and 18 columns — was being built into
    /// L01, the floor below it.
    /// </summary>
    [Fact]
    public void AMezzanineSheetFindsAMezzanineStoreyThatCarriesNoLevelNumber()
    {
        var sheet = PlanSheetNaming.Parse(
            "Str-Structural Plan - DXF-S2-08_2_LEVEL 1 MEZZ- PART PLAN CONCRETE OUTLINE Copy 1.dxf");
        var stories = new[] { "L02", "Mezz", "L01", "P1" };

        Assert.True(sheet.IsMezzanine);
        Assert.Equal(new[] { "Mezz" }, PlanSheetNaming.MatchStories(sheet, stories));
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
    public void TheBaseGapIsHonouredButNeverBecomesAStoreyHeight()
    {
        // 31168's shape exactly: the base is parked 1,000ft below the structure and the distance
        // is absorbed into the lowest storey's height. Ignoring the base lifts the whole model
        // 1,000ft; honouring it without capping that storey turns its walls into 1,100ft spikes.
        string[] realShape =
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"LEVEL 1\"  HEIGHT 144",
            "  STORY \"P1\"  HEIGHT 13366.23",
            "  STORY \"Base\"  ELEV -12000",
            "",
        };

        var stories = E2kDocument.Parse(realShape).ReadStories();
        var parkade = stories.Single(s => s.Name == "P1");

        // Top of the lowest storey sits where ETABS says it does, just above the ground.
        Assert.Equal(1366.23, parkade.Elevation, 2);

        // ...and it is a storey, not a thousand-foot wall.
        Assert.True(parkade.Elevation - parkade.ElevationBelow <= 480,
            $"lowest storey spans {parkade.Elevation - parkade.ElevationBelow:0}in");

        Assert.Equal(1510.23, stories.Single(s => s.Name == "LEVEL 1").Elevation, 2);
        Assert.Equal(1630.23, stories.Single(s => s.Name == "LEVEL 2").Elevation, 2);
    }

    [Fact]
    public void TheStoreyListEtabsReadsCarriesNoThousandFootStorey()
    {
        // Reading around the parked base was not enough. The storey list is what ETABS builds
        // from, so on import every member on that storey was extruded a thousand feet down and the
        // parkade arrived as a solid block half the height of the building.
        string[] parked =
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"LEVEL 1\"  HEIGHT 120",
            "  STORY \"P1\"  HEIGHT 13366.23",
            "  STORY \"Base\"  ELEV -12000",
            "",
        };

        var doc = E2kDocument.Parse(parked);
        var before = doc.ReadStories().ToDictionary(s => s.Name, s => s.Elevation);

        Assert.True(doc.NormaliseBaseStorey());

        // The lowest storey is a storey now...
        var lowest = doc.ReadStories().Single(s => s.Name == "P1");
        Assert.True(lowest.Elevation - lowest.ElevationBelow <= 480,
            $"lowest storey still spans {lowest.Elevation - lowest.ElevationBelow:0}in");

        // ...and nothing moved: the base rose by exactly as much as the storey shrank.
        foreach (var storey in doc.ReadStories())
            Assert.Equal(before[storey.Name], storey.Elevation, 3);

        // A model whose base is already sane is left alone.
        var healthy = E2kDocument.Parse(new[]
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"L02\"  HEIGHT 120",
            "  STORY \"L01\"  HEIGHT 144",
            "  STORY \"Base\"  ELEV -47",
            "",
        });
        Assert.False(healthy.NormaliseBaseStorey());
    }

    [Fact]
    public void ATowerStoreyStandsOnItsOwnTowersFloorNotOnTheOneAnInchBelow()
    {
        // 31168's upper levels, to scale: three towers share one storey list, so every distinct
        // floor elevation across the site becomes a storey. Tower B's 34th floor sits 2" above
        // tower A's, which makes "B-LEVEL 34" a 2"-tall storey. Reading that HEIGHT as a wall
        // height gave tower B two-inch wafers hanging a storey above the floor below — 78 of
        // 31168's 897 panels. Towers A and B interleave at 4.67ft and 5.0ft here, so no gap
        // threshold can tell a real storey from a duplicate floor; only the name can.
        string[] siteModel =
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"B-LEVEL 34\"  HEIGHT 2",
            "  STORY \"A-LEVEL 34\"  HEIGHT 120",
            "  STORY \"B-LEVEL 33\"  HEIGHT 37",
            "  STORY \"A-LEVEL 33\"  HEIGHT 114",
            "  STORY \"B-LEVEL 32\"  HEIGHT 5",
            "  STORY \"A-LEVEL 32\"  HEIGHT 76",
            "  STORY \"LEVEL 31\"  HEIGHT 116",
            "  STORY \"Base\"  ELEV 0",
            "",
        };

        var stories = E2kDocument.Parse(siteModel).ReadStories();
        double Span(string name) => stories.Single(s => s.Name == name).Elevation
                                  - stories.Single(s => s.Name == name).ElevationBelow;

        // B34 stands on B33, 10.2ft below — not on A34, two inches below.
        Assert.Equal(stories.Single(s => s.Name == "B-LEVEL 33").Elevation,
                     stories.Single(s => s.Name == "B-LEVEL 34").ElevationBelow, 3);
        Assert.Equal(122, Span("B-LEVEL 34"), 3);

        // A34 likewise stands on A33, not on the B storey between them.
        Assert.Equal(stories.Single(s => s.Name == "A-LEVEL 33").Elevation,
                     stories.Single(s => s.Name == "A-LEVEL 34").ElevationBelow, 3);
        Assert.Equal(157, Span("A-LEVEL 34"), 3);

        // A tower's lowest storey has no earlier storey of its own, so it stands on the shared
        // podium below — stepping past the other tower's storey five inches under it.
        Assert.Equal(116, stories.Single(s => s.Name == "B-LEVEL 32").ElevationBelow, 3);

        // No storey in a site model may come out shorter than a wall could be.
        foreach (var storey in stories)
            Assert.True(storey.Elevation - storey.ElevationBelow >= 60,
                $"{storey.Name} spans {storey.Elevation - storey.ElevationBelow:0}in — a wafer, not a storey.");
    }

    [Fact]
    public void ASingleBuildingModelStillMeasuresStoreysFromTheFloorBelow()
    {
        // The tower rule must not disturb a model whose storeys are simply stacked.
        string[] plain =
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"L03\"  HEIGHT 120",
            "  STORY \"L02\"  HEIGHT 120",
            "  STORY \"L01\"  HEIGHT 144",
            "  STORY \"Base\"  ELEV 0",
            "",
        };

        var stories = E2kDocument.Parse(plain).ReadStories();
        Assert.Equal(144, stories.Single(s => s.Name == "L01").Elevation, 3);
        Assert.Equal(0, stories.Single(s => s.Name == "L01").ElevationBelow, 3);
        Assert.Equal(264, stories.Single(s => s.Name == "L02").Elevation, 3);
        Assert.Equal(144, stories.Single(s => s.Name == "L02").ElevationBelow, 3);
    }

    [Fact]
    public void WallsMeetingAtACornerShareAJoint()
    {
        // Read straight off a drawing, each wall's centreline stops half the other wall's thickness
        // short of the corner, so an L is two panels with a gap between them and no connection.
        // "We can't have a wall go from here to here and then another one from here to here."
        var walls = new[]
        {
            new WallAxis(new DxfPoint(0, 6), new DxfPoint(114, 6), 12, "JBP_V-WALL"),    // stops 6" short
            new WallAxis(new DxfPoint(114, 12), new DxfPoint(114, 120), 12, "JBP_V-WALL"),
        };

        var joined = WallNetwork.Connect(walls);

        var ends = joined.SelectMany(w => new[] { w.Start, w.End }).ToList();
        Assert.Contains(ends, p => ends.Count(q => q.DistanceTo(p) < 1e-6) >= 2);

        var (connected, total) = WallNetwork.CountConnectedEnds(joined);
        Assert.Equal(2, connected);
        Assert.Equal(4, total);
    }

    [Fact]
    public void AWallRunningIntoAnotherSplitsItSoTheTeeHasAJoint()
    {
        var walls = new[]
        {
            new WallAxis(new DxfPoint(0, 0), new DxfPoint(240, 0), 12, "JBP_V-WALL"),
            new WallAxis(new DxfPoint(120, 0), new DxfPoint(120, 96), 12, "JBP_V-WALL"),
        };

        var joined = WallNetwork.Connect(walls);

        // The through wall is cut at the junction, so three members meet there: its two halves and
        // the stem running into it. Before this the stem simply stopped against an unbroken wall
        // and shared nothing with it.
        Assert.Equal(3, joined.Count);
        Assert.Equal(3, joined.Count(w => w.Start.DistanceTo(new DxfPoint(120, 0)) < 1e-6
                                       || w.End.DistanceTo(new DxfPoint(120, 0)) < 1e-6));
    }

    [Fact]
    public void ADoorwayIsFoundAsAnOpeningRatherThanClosedUp()
    {
        // The engineer's rule: the wall stops at the opening, and a header spans over it. The gap
        // must not be closed — on 31168 these measure 36-48", one tight cluster, nothing below 18".
        var walls = new[]
        {
            new WallAxis(new DxfPoint(0, 0), new DxfPoint(100, 0), 12, "JBP_V-WALL"),
            new WallAxis(new DxfPoint(142, 0), new DxfPoint(260, 0), 12, "JBP_V-WALL"),
        };

        var openings = WallNetwork.FindOpenings(walls, 24, 72);

        var opening = Assert.Single(openings);
        Assert.Equal(42, opening.Span, 3);

        // ...and the walls either side are left where they are.
        var joined = WallNetwork.Connect(walls);
        Assert.Equal(2, joined.Count);
        Assert.All(joined, w => Assert.True(w.Length > 90));
    }

    [Fact]
    public void AWallDrawnAsTwoConcentricRingsIsOneWallNotTwoEnormousOnes()
    {
        // A basement perimeter wall: drafting closes the outer face and the inner face separately.
        // Taken singly each is a building-sized rectangle and both were discarded, which is why
        // "below grade, the basement walls are missing".
        static IEnumerable<DxfSegment> Ring(double x0, double y0, double x1, double y1)
        {
            var c = new[] { new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1) };
            for (int i = 0; i < 4; i++) yield return new DxfSegment("JBP_B_WALL", c[i], c[(i + 1) % 4]);
        }

        var set = StructuralPlanClassifier.Classify(
            Ring(0, 0, 1200, 900).Concat(Ring(12, 12, 1188, 888)));   // 12" band

        Assert.NotEmpty(set.Walls);
        Assert.All(set.Walls, w => Assert.Equal(12, w.Thickness, 1));
        Assert.True(set.Walls.Sum(w => w.Length) > 3000,
            $"the perimeter should come through as wall, got {set.Walls.Sum(w => w.Length):0}\" of it.");
    }

    [Fact]
    public void OnlyAColumnTheDrawingDrewWithACurveIsRound()
    {
        // A chamfered square defeats every shape test: square bounding box, fills pi/4 of it, and
        // scores above 0.95 on perimeter efficiency — indistinguishable from a circle by geometry.
        // On 31168 that made 160 of them 10"-diameter round columns while the drawings hold no 10"
        // arc at all. What the drawing was drawn with is the fact; the shape is only an inference.
        static IEnumerable<DxfSegment> Chamfered(double cx, double cy, double half, double chamfer)
        {
            var pts = new List<DxfPoint>
            {
                new(cx - half + chamfer, cy - half), new(cx + half - chamfer, cy - half),
                new(cx + half, cy - half + chamfer), new(cx + half, cy + half - chamfer),
                new(cx + half - chamfer, cy + half), new(cx - half + chamfer, cy + half),
                new(cx - half, cy + half - chamfer), new(cx - half, cy - half + chamfer),
            };
            for (int i = 0; i < pts.Count; i++)
                yield return new DxfSegment("JBP_V_COL", pts[i], pts[(i + 1) % pts.Count]);
        }

        var chamferedOnly = StructuralPlanClassifier.Classify(Chamfered(0, 0, 5, 1.5));
        var square = Assert.Single(chamferedOnly.Columns);
        Assert.False(square.IsRound, "a chamfered square column must not be modelled as round.");

        // ...and a real circle, drawn as arcs, is.
        var circle = DxfPlanReader.TessellateArc("JBP_V_COL", 500, 500, 8, 0, 180)
            .Concat(DxfPlanReader.TessellateArc("JBP_V_COL", 500, 500, 8, 180, 360));

        var drawnRound = StructuralPlanClassifier.Classify(circle);
        var round = Assert.Single(drawnRound.Columns);
        Assert.True(round.IsRound, "a column drawn with arcs must be modelled as round.");
        Assert.Equal(16, round.Depth, 0);
        Assert.Equal(0, round.AxisAngleDegrees, 3);
    }

    [Fact]
    public void ASmallRingStandingOnItsOwnIsNotAFloorPlate()
    {
        // Slab-edge linework closes into little rings that are not floors. Left in, each one
        // draws in ETABS as a scrap of slab hanging in space: 7 of 31138's 14 plates were
        // 52-68 sq ft, against a real tower floor of 9,666.
        static IEnumerable<DxfSegment> Rect(double x0, double y0, double x1, double y1)
        {
            var c = new[] { new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1) };
            for (int i = 0; i < 4; i++)
                yield return new DxfSegment("JBP_C_SLABEDG", c[i], c[(i + 1) % 4]);
        }

        var scrap = Rect(0, 0, 120, 120);            // 100 sq ft — a ring, but not a floor
        var floor = Rect(500, 500, 1340, 1340);      // 4,900 sq ft

        var set = StructuralPlanClassifier.Classify(scrap.Concat(floor));

        Assert.Single(set.Slabs);
        Assert.True(set.Slabs[0].Area > 100000, "the plate kept should be the real floor.");
        Assert.Contains(set.Flags, f => f.Contains("too small for a floor plate", StringComparison.OrdinalIgnoreCase));
    }

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

        // Joints carry plan position only — ETABS takes elevation from the storey assigned, and a
        // number in the third slot is read as an offset from it, not as an elevation.
        var points = doc.LinesOf("POINT COORDINATES");
        var mine = points.Where(l => l.Contains("\"KP")).ToList();
        Assert.NotEmpty(mine);
        Assert.All(mine, l => Assert.Equal(4, l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length));

        // The wall is two plan joints repeated, and it is assigned to the storey it stands on.
        Assert.Contains(areas, l => l.Contains("KW1") && l.TrimEnd().EndsWith("1  1  0  0"));
        Assert.Contains(doc.LinesOf("AREA ASSIGNS"), l => l.Contains("KW1") && l.Contains("LEVEL 3"));

        // A column is one plan joint, rising a storey.
        Assert.Contains(lines, l => l.Contains("KC1") && l.TrimEnd().EndsWith("1"));
    }
}
