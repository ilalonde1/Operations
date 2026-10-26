using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A FLOOR THE DRAWING ZONES BY THICKNESS IS MODELLED AS ITS ZONES (intake step 183, 2026-10-25). 31065's L1 prints 900, 500,
/// 600 and 300 inside one 19,352 sq ft floor, its zones divided by the floor's own pen, its walls and dashed lines; the
/// classifier refused the four call-outs and kept the 12" default. WHAT THIS COVERS: the cut (a solid line of the outline's
/// pen, a dashed run, a gap a word stands in, a stray dash past a break, a face printing nothing taking its neighbour, a
/// floor printing one thickness left alone) and the classifier's replacement (zones that tile the floor replace it, each
/// priced; zones that do not are refused and the floor kept). WHAT IT DOES NOT: the reader's choice of which lines reach the
/// cut, and the e2k; a same-class fault it would not catch is a dimension drawn in the outline's pen across a zone, which
/// these synthetic floors do not draw (measured on the six sets, docs/pdf-intake/step183-prediction.md).
/// </summary>
public sealed class AFloorTheDrawingZonesIsModelledAsItsZonesTests
{
    private const double Heavy = 8.0, Thin = 5.0, Gap = 355.6;

    private static PlanLoop Floor(double w, double h)
        => new("SLAB", new List<DxfPoint> { new(0, 0), new(w, 0), new(w, h), new(0, h) }, closedExactly: true);

    private static (IReadOnlyList<DxfPoint> Line, double Width) Solid(double x0, double y0, double x1, double y1, double pen = Heavy)
        => (new List<DxfPoint> { new(x0, y0), new(x1, y1) }, pen);

    private static List<(IReadOnlyList<DxfPoint> Line, double Width)> Ring(double w, double h)
        => [Solid(0, 0, w, 0), Solid(w, 0, w, h), Solid(w, h, 0, h), Solid(0, h, 0, 0)];

    /// <summary>A dashed line along y from x0 to x1: 400 mm dashes, 200 mm gaps, in the thin pen.</summary>
    private static IEnumerable<(IReadOnlyList<DxfPoint> Line, double Width)> Dashed(double y, double x0, double x1)
    {
        for (double x = x0; x + 400 <= x1 + 1e-6; x += 600)
            yield return (new List<DxfPoint> { new(x, y), new(x + 400, y) }, Thin);
    }

    private static SlabZones.Callout Says(double mm, double x, double y) => new(new DxfPoint(x, y), mm, IsMetric: true);

    private static IReadOnlyList<SlabZones.Zone>? Cut(PlanLoop floor, IEnumerable<(IReadOnlyList<DxfPoint>, double)> lines, IEnumerable<SlabZones.Callout> says,
        out string why, IReadOnlyList<(string, double, double, double, double)>? words = null)
        => SlabZones.Cut(floor, lines.ToList(), says.ToList(), 1.0, 200, 48 * 25.4, Gap, out why, words: words);

    /// <summary>The zones tile the floor: their areas sum to its area, and no zone's middle stands inside another zone.</summary>
    private static void AssertTheZonesTileTheFloor(PlanLoop floor, IReadOnlyList<SlabZones.Zone> zones)
    {
        Assert.Equal(floor.Area, zones.Sum(z => z.Ring.Area), floor.Area * 0.002);
        foreach (var z in zones)
            Assert.DoesNotContain(zones, o => !ReferenceEquals(o, z) && LoopGeometry.PointInPolygon(SlabZones.InteriorPoint(z.Ring), o.Ring.Points));
    }

    /// <summary>
    /// A ZONE INSIDE ANOTHER IS NOT WRITTEN TWICE (30993 L2 south, 2026-10-26): ten zones of 2-8 sq ft - cells of the big
    /// zone's own thickness standing in a hole of it - were written beside it, 30 sq ft of plate twice. The floor here:
    /// a 900 field, a 500 bay boxed off, and in the field a box within a box: the ring between them prints 500, the core
    /// inside it prints 900, the field's own thickness. The field's cells wrap the ring, the core stands in the ring's
    /// hole, and written as the field's outer ring the field already covers the core - so the core is not written again.
    /// ⚠ A PROPERTY TEST, NOT A REPRODUCTION: with the same-thickness drop removed this synthetic floor still passes - the
    /// island rule absorbs the box-in-box before the union is made - so it does not prove the fix. The fix is proven on the
    /// real witness instead (30993 L2 south through SlabPassTraceProbe: drop removed, the tiling check refuses the floor
    /// "26,454 of 26,424 - they do not tile it"; drop restored, 26,424 of 26,424), 2026-10-26.
    /// </summary>
    [Fact]
    public void AZoneInsideAnotherIsNotWrittenTwice()
    {
        static IEnumerable<(IReadOnlyList<DxfPoint>, double)> Box(double x0, double y0, double x1, double y1)
            => [Solid(x0, y0, x1, y0), Solid(x1, y0, x1, y1), Solid(x1, y1, x0, y1), Solid(x0, y1, x0, y0)];
        var lines = Ring(20000, 10000).Append(Solid(14000, 0, 14000, 10000))
            .Concat(Box(3000, 2000, 9000, 8000)).Concat(Box(5000, 4000, 7000, 6000)).ToList();
        var zones = Cut(Floor(20000, 10000), lines,
            [Says(900, 1500, 9000), Says(500, 17000, 5000), Says(500, 4000, 5000), Says(900, 6000, 5000)], out string why);

        Assert.True(zones is not null, why);
        AssertTheZonesTileTheFloor(Floor(20000, 10000), zones!);
    }

    [Fact]
    public void ALineOfTheOutlinesPenDividesTwoThicknesses()
    {
        var zones = Cut(Floor(20000, 10000), Ring(20000, 10000).Append(Solid(12000, 0, 12000, 10000)),
            [Says(900, 5000, 5000), Says(500, 16000, 5000)], out string why);

        Assert.True(zones is not null, why);
        Assert.Equal(2, zones!.Count);
        Assert.Equal(12000.0 * 10000, zones.Single(z => z.Printed == 900).Ring.Area, 0);
        Assert.Equal(8000.0 * 10000, zones.Single(z => z.Printed == 500).Ring.Area, 0);
    }

    [Fact]
    public void ADashedLineDividesThemTooAndAPartPrintingNothingTakesTheCallOutNearestIt()
    {
        // a dashed line at y 6000 across the whole floor; above it 600, below it nothing at the left and 900 to the right,
        // divided by a solid line at x 4000 - the left-below part prints nothing; the 900 is nearer its middle than the 600
        var lines = Ring(20000, 10000).Concat(Dashed(6000, 0, 20000)).Append(Solid(4000, 0, 4000, 6000)).ToList();
        var zones = Cut(Floor(20000, 10000), lines, [Says(600, 14000, 9000), Says(900, 6000, 3000)], out string why);

        Assert.True(zones is not null, why);
        // to a square metre: a dashed line's end is carried 1 mm past the line it stops at, so the two cross
        Assert.Equal(20000.0 * 4000, zones!.Where(z => z.Printed == 600).Sum(z => z.Ring.Area), 1e6);
        Assert.Equal(20000.0 * 6000, zones.Where(z => z.Printed == 900).Sum(z => z.Ring.Area), 1e6);
    }

    [Fact]
    public void ADashedLineBrokenForAWordIsOneLine()
    {
        // the dashes stop for 1.5 m where '2000' is written over the line; without the word the run is two runs, the right one
        // stops 1.5 m short of the floor's west edge, and nothing is divided
        var lines = Ring(20000, 10000).Concat(Dashed(6000, 0, 3000)).Concat(Dashed(6000, 4500, 20000)).ToList();
        var says = new[] { Says(600, 10000, 8000), Says(900, 10000, 3000) };
        IReadOnlyList<(string, double, double, double, double)> word = [("2000", 3000, 5900, 4500, 6300)];

        var zones = Cut(Floor(20000, 10000), lines, says, out string why, word);
        Assert.True(zones is not null, why);
        Assert.Equal(20000.0 * 4000, zones!.Where(z => z.Printed == 600).Sum(z => z.Ring.Area), 0);
    }

    /// <summary>
    /// A DASHED LINE AT AN ANGLE IS A LINE TOO: 31130 L14 outlines its 16" areas with dashes parallel to the slab's slanted
    /// edges, and a join of horizontal and vertical runs left those areas open on two sides.
    /// </summary>
    [Fact]
    public void ADashedLineOnASlantDividesTheFloor()
    {
        // a dashed line from (0, 2000) to (20000, 8000): 400 mm dashes, 200 mm gaps, along the slant
        var slant = new List<(IReadOnlyList<DxfPoint>, double)>();
        double len = Math.Sqrt(20000.0 * 20000 + 6000.0 * 6000), ux = 20000 / len, uy = 6000 / len;
        for (double t = 0; t + 400 <= len; t += 600)
            slant.Add((new List<DxfPoint> { new(t * ux, 2000 + t * uy), new((t + 400) * ux, 2000 + (t + 400) * uy) }, Thin));
        var zones = Cut(Floor(20000, 10000), Ring(20000, 10000).Concat(slant), [Says(600, 10000, 9000), Says(900, 10000, 1000)], out string why);

        Assert.True(zones is not null, why);
        Assert.Equal(2, zones!.Count);
        Assert.Equal(20000.0 * 10000 - 20000.0 * 5000, zones.Single(z => z.Printed == 600).Ring.Area, 2e6);
    }

    /// <summary>
    /// A SHORT STROKE IN THE OUTLINE'S OWN PEN IS A PIECE OF A BOUNDARY: 31130 L16's tower-above outline steps down 0.9 m,
    /// one short stroke of the outline's pen, which taken only as a dash (no run to join) was dropped - the outline stood
    /// open at the step and the 18" centre ran into the 10" round it.
    /// </summary>
    [Fact]
    public void AShortStepInTheOutlinesPenClosesTheBoundary()
    {
        // a boundary of the outline's pen across the floor at y 6000, stepping down to y 5100 at x 10000 by one 900 mm stroke
        var lines = Ring(20000, 10000)
            .Append(Solid(0, 6000, 10000, 6000)).Append(Solid(10000, 6000, 10000, 5100)).Append(Solid(10000, 5100, 20000, 5100)).ToList();
        var zones = Cut(Floor(20000, 10000), lines, [Says(900, 5000, 8000), Says(300, 15000, 2000)], out string why);

        Assert.True(zones is not null, why);
        Assert.Equal(2, zones!.Count);
        Assert.Equal(10000.0 * 4000 + 10000.0 * 4900, zones.Single(z => z.Printed == 900).Ring.Area, 1e6);
    }

    [Fact]
    public void AStrayDashPastABreakEndsTheLine()
    {
        // one dash at the west edge, a 1.3 m break with no word in it, then the run: the stray dash is the line's last
        var lines = Ring(20000, 10000).Append((new List<DxfPoint> { new(0, 6000), new(400, 6000) }, Thin))
            .Concat(Dashed(6000, 1700, 20000)).ToList();
        var zones = Cut(Floor(20000, 10000), lines, [Says(600, 10000, 8000), Says(900, 10000, 3000)], out string why);

        Assert.True(zones is not null, why);
        Assert.Equal(2, zones!.Count);
    }

    /// <summary>
    /// AN ISLAND IN ONE CELL IS THAT CELL'S: a box drawn inside one region, printing a thickness of its own, cannot be cut
    /// round in a single cell - 31065 L1 north stood down whole for it. It is taken into the region and the reason says so.
    /// </summary>
    [Fact]
    public void ABoxInsideOneRegionIsTakenIntoItAndTheFloorStillZones()
    {
        var lines = Ring(20000, 10000).Append(Solid(12000, 0, 12000, 10000))
            // a solid box, its sides longer than a dash (four gaps) so they are drawn lines and not dashes
            .Concat([Solid(2000, 2000, 4500, 2000), Solid(4500, 2000, 4500, 4500), Solid(4500, 4500, 2000, 4500), Solid(2000, 4500, 2000, 2000)]).ToList();
        var zones = Cut(Floor(20000, 10000), lines, [Says(900, 8000, 8000), Says(500, 16000, 5000), Says(200, 3250, 3250)], out string why);

        Assert.True(zones is not null, why);
        Assert.Equal(12000.0 * 10000, zones!.Where(z => z.Printed == 900).Sum(z => z.Ring.Area), 1e6);
        Assert.DoesNotContain(zones, z => z.Printed == 200);
        Assert.Contains("taken into the zone round them", why, StringComparison.Ordinal);
        AssertTheZonesTileTheFloor(Floor(20000, 10000), zones);
    }

    [Fact]
    public void AFloorPrintingOneThicknessIsNotCut()
    {
        var zones = Cut(Floor(20000, 10000), Ring(20000, 10000).Append(Solid(12000, 0, 12000, 10000)),
            [Says(900, 5000, 5000), Says(900, 16000, 5000)], out string why);
        Assert.Null(zones);
        Assert.Contains("fewer than two", why, StringComparison.Ordinal);
    }

    // ---- the classifier's end of it -------------------------------------------------------------------------------

    private const string SlabLayer = "JBP_C_SLABEDG";

    private static IEnumerable<DxfSegment> Closed(string layer, int id, double x0, double y0, double x1, double y1)
    {
        var c = new[] { new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1) };
        for (int i = 0; i < 4; i++) yield return new DxfSegment(layer, c[i], c[(i + 1) % 4]) { OfClosedOutline = true, ClosedOutlineId = id };
    }

    private static PlanClassificationOptions Options() => new()
    {
        SlabLayerPatterns = new[] { SlabLayer },
        WallLayerPatterns = new[] { "JBP_V-WALL" },
        ColumnLayerPatterns = new[] { "JBP_V_COL" },
    };

    private static DxfPositionedTag Tag(string text, double x, double y) => new(text, new DxfPoint(x, y), "JBP_TAG_FLOORS", text);

    /// <summary>
    /// THE FLOOR STAYS ONE PLATE AND CARRIES ITS ZONES: everything that asks where a floor is asks it of the floor (replaced
    /// by its zones, another sheet's partial reading of 31138's L1 lay inside none of them and was modelled again, +6,816
    /// sq ft); only the composer writes the zones.
    /// </summary>
    [Fact]
    public void TheZonesThatTileAFloorAreCarriedByItEachAtItsOwnThickness()
    {
        var segments = Closed(SlabLayer, 1, 0, 0, 3000, 2400)
            .Concat(Closed(DxfExporter.SlabZoneLayer, 2, 0, 0, 1800, 2400))
            .Concat(Closed(DxfExporter.SlabZoneLayer, 3, 1800, 0, 3000, 2400)).ToList();
        var set = StructuralPlanClassifier.Classify(segments, Options(), sheet: null,
            tags: new[] { Tag("36\" SLAB", 900, 1200), Tag("12\" SLAB", 2400, 1200) });

        var floor = Assert.Single(set.Slabs);
        Assert.Equal(3000.0 * 2400, floor.Area, 0);
        Assert.Equal(36, floor.ThicknessInchesFromTag);   // the zone of the most ground, for what reads the floor whole
        Assert.Equal(2, floor.Zones.Count);
        Assert.Equal(36, floor.Zones.Single(s => s.Points.Average(p => p.X) < 1800).ThicknessInchesFromTag);
        Assert.Equal(12, floor.Zones.Single(s => s.Points.Average(p => p.X) > 1800).ThicknessInchesFromTag);
        Assert.DoesNotContain(set.Flags, f => f.Contains("different thickness call-outs", StringComparison.Ordinal));
        Assert.Contains(set.Flags, f => f.Contains("modelled as its 2 zones", StringComparison.Ordinal));
    }

    /// <summary>
    /// A ZONE THAT PRINTS NOTHING TAKES THE CALL-OUT NEAREST IT, as the page read made it - priced only by what stands inside,
    /// it fell to the 12" default (31130 P2, 6,182 sq ft).
    /// </summary>
    [Fact]
    public void AZonePrintingNothingTakesTheNearestCallOutNotTheDefault()
    {
        var segments = Closed(SlabLayer, 1, 0, 0, 3000, 2400)
            .Concat(Closed(DxfExporter.SlabZoneLayer, 2, 0, 0, 1800, 2400))
            .Concat(Closed(DxfExporter.SlabZoneLayer, 3, 1800, 0, 2400, 2400))
            .Concat(Closed(DxfExporter.SlabZoneLayer, 4, 2400, 0, 3000, 2400)).ToList();
        var set = StructuralPlanClassifier.Classify(segments, Options(), sheet: null,
            tags: new[] { Tag("36\" SLAB", 900, 1200), Tag("12\" SLAB", 2700, 1200) });

        var floor = Assert.Single(set.Slabs);
        Assert.Equal(3, floor.Zones.Count);
        Assert.DoesNotContain(floor.Zones, z => z.ThicknessInchesFromTag is null);
        Assert.Equal(12, floor.Zones.Single(z => Math.Abs(z.Points.Average(p => p.X) - 2100) < 1).ThicknessInchesFromTag);
    }

    /// <summary>
    /// A ZONE IS PRICED AMONG THE SHEET'S OTHER PLATES, AS ITS FLOOR WAS: a call-out inside a smaller ring of its own is that
    /// ring's (31130 L14: the «16"» stand in rings of a pen the zoning does not divide by; priced among the zones alone the
    /// floor's zone took 16" over 10,590 sq ft, where it prints the 9" field).
    /// </summary>
    [Fact]
    public void ACallOutInsideASmallerRingIsTheRingsNotTheZones()
    {
        var segments = Closed(SlabLayer, 1, 0, 0, 3000, 2400)
            .Concat(Closed(SlabLayer, 5, 300, 300, 1200, 900))             // a 16" ring inside the west zone
            .Concat(Closed(DxfExporter.SlabZoneLayer, 2, 0, 0, 1800, 2400))
            .Concat(Closed(DxfExporter.SlabZoneLayer, 3, 1800, 0, 3000, 2400)).ToList();
        var set = StructuralPlanClassifier.Classify(segments, Options(), sheet: null,
            tags: new[] { Tag("9\" SLAB", 900, 1800), Tag("16\" SLAB", 600, 600), Tag("16\" SLAB", 900, 600), Tag("12\" SLAB", 2400, 1200) });

        var floor = set.Slabs.Single(s => s.Zones.Count > 0);
        Assert.Equal(9, floor.Zones.Single(z => z.Points.Average(p => p.X) < 1800).ThicknessInchesFromTag);
    }

    [Fact]
    public void ZonesThatDoNotTileTheFloorLeaveItWhole()
    {
        var segments = Closed(SlabLayer, 1, 0, 0, 3000, 2400)
            .Concat(Closed(DxfExporter.SlabZoneLayer, 2, 0, 0, 1200, 2400))
            .Concat(Closed(DxfExporter.SlabZoneLayer, 3, 1800, 0, 3000, 2400)).ToList();
        var set = StructuralPlanClassifier.Classify(segments, Options(), sheet: null,
            tags: new[] { Tag("36\" SLAB", 600, 1200), Tag("12\" SLAB", 2400, 1200) });

        var floor = Assert.Single(set.Slabs);
        Assert.Equal(3000.0 * 2400, floor.Area, 0);
        Assert.Empty(floor.Zones);
        Assert.Contains(set.Flags, f => f.Contains("the floor was kept whole", StringComparison.Ordinal));
    }

    // ---- between them: a plate keeps every property it carries --------------------------------------------------

    /// <summary>
    /// THE ZONES MOVE WITH THE FLOOR. Placing a sheet on the model's grid rebuilt each plate with its thickness and nothing
    /// else, and every zoned floor of 31065 reached the composer whole - the report said "modelled as its zones", the e2k
    /// was byte-identical without them. The class: a plate rebuilt without all it carries. So this also names PlanLoop's
    /// settable properties - a new one fails here until the copies are looked at (PlanGeometryTransform.Move; the seam trim
    /// in DxfToEtabsService leaves zones behind on purpose and says so).
    /// </summary>
    [Fact]
    public void AFloorPlacedOnTheGridKeepsItsZonesMovedWithIt()
    {
        var floor = new PlanLoop("SLAB", Floor(3000, 2400).Points, true)
        {
            ThicknessInchesFromTag = 36,
            Zones = [new PlanLoop("SLAB", Floor(1800, 2400).Points, true) { ThicknessInchesFromTag = 36 }],
        };
        var set = new PlanGeometrySet();
        set.Slabs.Add(floor);

        var moved = Assert.Single(PlanGeometryTransform.Apply(set, new AnnotationOverlay.Frame(0, 1000, 500)).Slabs);
        var zone = Assert.Single(moved.Zones);
        Assert.Equal(36, zone.ThicknessInchesFromTag);
        Assert.Equal(new DxfPoint(1000, 500), zone.Points[0]);

        var settable = typeof(PlanLoop).GetProperties().Where(p => p.SetMethod is not null).Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "ThicknessInchesFromTag", "Zones" }, settable);
    }

    // ---- the composer's end of it ---------------------------------------------------------------------------------

    private static readonly string[] Reference =
    {
        "$ PROGRAM INFORMATION",
        "  PROGRAM  \"ETABS\"  VERSION \"21.2.0\"",
        "",
        "$ CONTROLS",
        "  UNITS  \"KIP\"  \"IN\"  \"F\"",
        "",
        "$ STORIES - IN SEQUENCE FROM TOP",
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

    private static PlanLoop Box(double x0, double y0, double x1, double y1, double? inches = null)
        => new("JBP_C_SLABEDG", new[] { new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1) }, true) { ThicknessInchesFromTag = inches };

    private static PlanGeometrySet Sheet(PlanLoop plate)
    {
        var set = new PlanGeometrySet();
        set.Slabs.Add(plate);
        foreach (var (x, y) in new[] { (100.0, 100.0), (400.0, 100.0), (100.0, 400.0), (400.0, 400.0) })
            set.Columns.Add(new ColumnFootprint(new DxfPoint(x, y), 24, 24, "JBP_V_COL"));
        return set;
    }

    [Fact]
    public void AZonedFloorIsWrittenAsItsZonesAndAnotherReadingOfItIsStillTheSameFloor()
    {
        var doc = E2kDocument.Parse(Reference);
        var l2 = doc.ReadStories().Single(s => s.Name == "LEVEL 2");
        // a 40 x 40 ft floor zoned 36" (west 30 ft) and 12" (east 10 ft); another sheet reads the east 25 ft of it again -
        // inside the floor, astride both zones, inside neither
        var whole = Box(0, 0, 480, 480);
        var zoned = new PlanLoop(whole.Layer, whole.Points, true) { ThicknessInchesFromTag = 36, Zones = [Box(0, 0, 360, 480, 36), Box(360, 0, 480, 480, 12)] };
        var summary = E2kGeometryComposer.Compose(doc, new[]
        {
            new StoryPlacement(l2, Sheet(zoned), "level-2.dxf"),
            new StoryPlacement(l2, Sheet(Box(180, 0, 480, 480)), "level-2-east.dxf"),
        });

        var floors = doc.LinesOf("AREA CONNECTIVITIES").Where(l => l.Contains(" FLOOR ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, floors.Count);
        var assigns = doc.LinesOf("AREA ASSIGNS").Where(l => l.Contains("AREAASSIGN", StringComparison.Ordinal)).ToList();
        Assert.Contains(assigns, l => l.Contains("KOR-S36", StringComparison.Ordinal));
        Assert.Contains(assigns, l => l.Contains("KOR-S12", StringComparison.Ordinal));
        Assert.Contains(summary.Flags, f => f.Contains("level-2-east.dxf", StringComparison.Ordinal) && f.Contains("one floor, not two", StringComparison.Ordinal));
    }

    [Fact]
    public void AZoneLayerIsNotASlabEdgeAndChangesNothingWithoutAFloorToTile()
    {
        // zones and no floor: nothing is read as a floor from them
        var segments = Closed(DxfExporter.SlabZoneLayer, 2, 0, 0, 1800, 2400)
            .Concat(Closed(DxfExporter.SlabZoneLayer, 3, 1800, 0, 3000, 2400)).ToList();
        var set = StructuralPlanClassifier.Classify(segments, Options(), sheet: null,
            tags: new[] { Tag("36\" SLAB", 900, 1200), Tag("12\" SLAB", 2400, 1200) });
        Assert.Empty(set.Slabs);
    }
}
