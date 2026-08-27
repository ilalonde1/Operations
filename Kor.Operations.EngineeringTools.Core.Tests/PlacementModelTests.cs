using System.Globalization;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The placement model, one test per ruling banked by migration 057.
///
/// Every one of these was a separate fault found on 27 August, and they are one question: which
/// storey does a member belong to, and whose building is it? A storey is an ETABS named elevation;
/// a FLOOR is the physical level, which a site model names more than once because the engineer
/// names one level separately for different buildings; a BUILDING is sheet provenance first,
/// storey-name ownership second, and geometry only where both are silent.
/// </summary>
public sealed class PlacementModelTests
{
    private readonly ITestOutputHelper _out;
    public PlacementModelTests(ITestOutputHelper output) => _out = output;

    // A site whose storeys are named the way the engineer's 31168 model names them: the ground
    // floor after two of the three buildings standing on it, the parkade after nobody, and the
    // towers interleaved a few inches apart all the way up.
    private static string[] Site(params string[] body) => new[]
    {
        "$ STORIES - IN SEQUENCE FROM TOP",
        // Elevations, bottom up: Base 0, LEVEL P1 120, A-LEVEL 1 240, B-LEVEL 1 242,
        // LEVEL 1 MEZZ 290, A-LEVEL 2 360, B-LEVEL 2 364, A-LEVEL 3 480, B-LEVEL 3 484.
        // Real storeys 120 apart; the towers' pairs 2 and 4 apart, the way a site model
        // carries one floor twice.
        "  STORY \"B-LEVEL 3\"  HEIGHT 4",
        "  STORY \"A-LEVEL 3\"  HEIGHT 116",
        "  STORY \"B-LEVEL 2\"  HEIGHT 4",
        "  STORY \"A-LEVEL 2\"  HEIGHT 70",
        "  STORY \"LEVEL 1 MEZZ\"  HEIGHT 48",
        "  STORY \"B-LEVEL 1\"  HEIGHT 2",
        "  STORY \"A-LEVEL 1\"  HEIGHT 120",
        "  STORY \"LEVEL P1\"  HEIGHT 120",
        "  STORY \"Base\"  HEIGHT 0",
        "",
        "$ MATERIAL PROPERTIES",
        "  MATERIAL  \"65 MPa Walls\"    TYPE \"Concrete\"    GRADE \"x\"",
        "",
    }.Concat(body).ToArray();

    // ---------------------------------------------------------------------------------------
    // a-set-defines-its-own-shorthand
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ASetSpellsItsShorthandOutOnceAndTheShortFormMeansIt()
    {
        var glossary = SheetSetGlossary.Learn(new[]
        {
            "S2.13.1_1_LEVEL 1 MEZZ PLAN - CONCRETE OUTLINE - WEST (BLDG A & B).dxf",
            "S2.11.1_1_LEVEL 1 PLAN - CONCRETE OUTLINE - WEST.dxf",
            "S2.10.1_1_LEVEL 1 PLAN - CONCRETE OUTLINE - BLDG C.dxf",
        });

        var west = glossary.TagsFor(PlanSheetNaming.Parse(
            "S2.11.1_1_LEVEL 1 PLAN - CONCRETE OUTLINE - WEST.dxf"));

        _out.WriteLine("WEST = " + string.Join(", ", west));
        Assert.Equal(new[] { "A", "B" }, west);

        // A sheet that names its own buildings is never overruled by the glossary.
        var c = glossary.TagsFor(PlanSheetNaming.Parse(
            "S2.10.1_1_LEVEL 1 PLAN - CONCRETE OUTLINE - BLDG C.dxf"));
        Assert.Equal(new[] { "C" }, c);
    }

    [Fact]
    public void AWordThatStandsForTwoBuildingsTeachesNothing()
    {
        // "OUTLINE" beside BLDG A on one sheet and BLDG B on another is a coincidence, not
        // shorthand, and a rule that learned from it would hand every sheet in the set to A.
        var glossary = SheetSetGlossary.Learn(new[]
        {
            "S1_1_LEVEL 4 PLAN - BLDG A - OUTLINE.dxf",
            "S2_1_LEVEL 4 PLAN - BLDG B - OUTLINE.dxf",
        });

        Assert.DoesNotContain("OUTLINE", glossary.Meanings.Keys, StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------
    // issued-sheets-beat-kept-views
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AnIssuedSheetCarriesItsNumberAndAKeptViewDoesNot()
    {
        var v = DrawingVocabulary.Default;

        Assert.True(v.IsIssuedSheetName("--Structural Plan - S2.22.1_1_LEVEL 33 PLAN - BLDG A.dxf"));
        Assert.True(v.IsIssuedSheetName("--Structural Plan - S3.03_2_KEY PLAN - CORE WALLS - BLDG C.dxf"));

        // The uncropped working views the drafter kept. B-LEVEL 33 held 73 columns where the
        // tower has 24, because it draws every building standing at that elevation.
        Assert.False(v.IsIssuedSheetName("--Structural Plan - B-LEVEL 33.dxf"));
        Assert.False(v.IsIssuedSheetName("--Structural Plan - LEVEL 26.dxf"));
        Assert.False(v.IsIssuedSheetName("--Structural Plan - LEVEL 1 PLAN - CONCRETE OUTLINE.dxf"));
    }

    // ---------------------------------------------------------------------------------------
    // a-storey-height-is-measured-up-one-building
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AStoreyHeightIsMeasuredUpOneBuildingNotAcrossASite()
    {
        var doc = E2kDocument.Parse(Site());

        // Measured across the site, consecutive elevations are the same floor drafted twice: the
        // A/B pairs sit 2 and 4 in apart between real 116-118 in storeys, so a site-wide median
        // lands near a quarter of a storey. Grouped by building first it is half of one.
        double tolerance = doc.SameFloorTolerance();
        _out.WriteLine($"same-floor tolerance: {tolerance:0.#} in");

        Assert.InRange(tolerance, 55, 65);

        // And the pairs therefore group as ONE floor.
        var floors = doc.FloorOfStorey();
        Assert.Equal(floors["A-LEVEL 2"], floors["B-LEVEL 2"]);
        Assert.Equal(floors["A-LEVEL 3"], floors["B-LEVEL 3"]);

        // Two real storeys never do.
        Assert.NotEqual(floors["A-LEVEL 2"], floors["A-LEVEL 3"]);
    }

    // ---------------------------------------------------------------------------------------
    // below-a-buildings-start-another-prefix-is-the-base
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AMemberRisesOntoTheSharedBaseEvenWhenItIsNamedForAnotherBuilding()
    {
        var doc = E2kDocument.Parse(Site());
        var stories = doc.ReadStories().ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        // Building C's parkade columns, drawn on the sheet titled for building C and standing on
        // LEVEL P1 — which is named for nobody. The floor above them is the ground floor, drafted
        // as A-LEVEL 1 and B-LEVEL 1 after two of the three buildings on it. Building C has no
        // storey of its own there, and refusing it sent these past the ground floor to the
        // mezzanine: LEVEL 1 shipped as a plate with nothing under it.
        var parkade = new PlanGeometrySet();
        parkade.Columns.Add(new ColumnFootprint(new DxfPoint(240, 240), 24, 24, "JBP_V_COL"));

        var summary = E2kGeometryComposer.Compose(doc, new[]
        {
            new StoryPlacement(stories["LEVEL P1"], parkade, "S2.05.1_1_LEVEL P1 PLAN - BLDG C.dxf")
            {
                SheetBuildingTag = "C",
                SheetBuildingTags = new[] { "C" },
                IsPerBuildingSheet = true,
            },
        });

        var on = Assert.Single(doc.StoreysByObject().Where(x => x.Key.StartsWith("KC", StringComparison.Ordinal)));
        _out.WriteLine($"{summary.Columns} column(s); building C's parkade column stands on {string.Join(", ", on.Value)}");

        Assert.Contains("A-LEVEL 1", on.Value, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("LEVEL 1 MEZZ", on.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AboveWhereItsBuildingStartsAMemberNeverCrossesIntoAnother()
    {
        var doc = E2kDocument.Parse(Site());
        var stories = doc.ReadStories().ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        // Tower A at its own level 2. B-LEVEL 2 sits 4 in above it and B-LEVEL 3 above that; the
        // member must take A-LEVEL 3, its own building's next floor, and nothing else. Letting a
        // member cross here once put six of tower B's headers on a tower A storey 130 ft away.
        var tower = new PlanGeometrySet();
        tower.Columns.Add(new ColumnFootprint(new DxfPoint(600, 600), 24, 24, "JBP_V_COL"));

        E2kGeometryComposer.Compose(doc, new[]
        {
            new StoryPlacement(stories["A-LEVEL 2"], tower, "S2.21.1_1_LEVEL 2 PLAN - BLDG A.dxf")
            {
                SheetBuildingTag = "A",
                SheetBuildingTags = new[] { "A" },
                IsPerBuildingSheet = true,
            },
        });

        var on = Assert.Single(doc.StoreysByObject().Where(x => x.Key.StartsWith("KC", StringComparison.Ordinal)));
        _out.WriteLine("tower A's level 2 column rises to " + string.Join(", ", on.Value));

        Assert.Contains("A-LEVEL 3", on.Value, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("B-LEVEL 3", on.Value, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("B-LEVEL 2", on.Value, StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------
    // one-floor-holds-one-of-each-member
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void OneFloorHoldsOneOfEachMemberAndTheSurplusAssignIsWhatGoes()
    {
        // The same plate assigned to both names of one floor — which is what a site-wide drawing
        // gets when the ground floor is drafted as A-LEVEL 1 and B-LEVEL 1 an inch and a half
        // apart, and what a borrowed floor looks like. ONE object, two assigns.
        var doc = E2kDocument.Parse(Site(
            "$ POINT COORDINATES",
            "  POINT \"KP1\"  0 0",
            "  POINT \"KP2\"  240 0",
            "  POINT \"KP3\"  240 240",
            "  POINT \"KP4\"  0 240",
            "",
            "$ AREA CONNECTIVITIES",
            "  AREA \"KF1\"  FLOOR  4  \"KP1\" \"KP2\" \"KP3\" \"KP4\"  0 0 0 0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KF1\"  \"A-LEVEL 1\"  SECTION \"Slab12\"  DIAPH \"D1\"",
            "  AREAASSIGN  \"KF1\"  \"B-LEVEL 1\"  SECTION \"Slab12\"  DIAPH \"D1\"",
            ""));

        int gone = doc.DropMembersDuplicatedOnOneFloor();
        _out.WriteLine($"{gone} surplus assign(s) removed");

        Assert.Equal(1, gone);

        // The object survives with exactly one assign, on the LOWER of the two.
        var where = doc.StoreysByObject();
        Assert.True(where.TryGetValue("KF1", out var stands), "the plate object itself must survive");
        Assert.Equal(new[] { "A-LEVEL 1" }, stands);

        // And a plate on two genuinely different floors is left alone — that is how ETABS repeats
        // a member up a building.
        var twoFloors = E2kDocument.Parse(Site(
            "$ POINT COORDINATES",
            "  POINT \"KP1\"  0 0", "  POINT \"KP2\"  240 0",
            "  POINT \"KP3\"  240 240", "  POINT \"KP4\"  0 240",
            "",
            "$ AREA CONNECTIVITIES",
            "  AREA \"KF1\"  FLOOR  4  \"KP1\" \"KP2\" \"KP3\" \"KP4\"  0 0 0 0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KF1\"  \"A-LEVEL 2\"  SECTION \"Slab12\"  DIAPH \"D1\"",
            "  AREAASSIGN  \"KF1\"  \"A-LEVEL 3\"  SECTION \"Slab12\"  DIAPH \"D2\"",
            ""));

        Assert.Equal(0, twoFloors.DropMembersDuplicatedOnOneFloor());
    }

    // ---------------------------------------------------------------------------------------
    // a-circle-the-set-draws-no-column-at
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ACircleDrawnAsAPolygonIsRememberedAsOne()
    {
        // Twelve points on a 10 in circle, drawn with no arc — the shape a grid bubble takes when
        // a Revit export writes it as two polyline semicircles and the reader closes them.
        var ring = Enumerable.Range(0, 12)
            .Select(i => i * Math.PI * 2 / 12)
            .Select(a => new DxfPoint(120 + 5 * Math.Cos(a), 120 + 5 * Math.Sin(a)))
            .ToList();

        var read = StructuralPlanClassifier.Classify(
            ring.Zip(ring.Skip(1).Append(ring[0]))
                .Select(p => new DxfSegment("JBP_V_COL", p.First, p.Second))
                .ToList(),
            new PlanClassificationOptions());

        var circle = Assert.Single(read.Columns);
        _out.WriteLine($"{circle.Width:0}x{circle.Depth:0} in, drawn as a polygon circle: {circle.DrawnAsAPolygonCircle}");

        Assert.True(circle.DrawnAsAPolygonCircle,
            "a twelve-sided ring filling pi/4 of a square box, with no arc, is what a polygonised circle looks like");
        Assert.InRange(circle.Width, 9, 11);
    }
}
