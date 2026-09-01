using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Pricing a finished ETABS model. Every expected number here is arithmetic done by hand on a
/// building small enough to check in your head — a ten-foot square bay, one storey ten feet high —
/// so a failure names the rule that broke rather than the digit that moved.
///
/// The model is in INCHES, which is what our generator writes and what the shipped 31168 files are.
/// </summary>
public sealed class E2kQuantityTakeoffTests
{
    private readonly ITestOutputHelper _out;
    public E2kQuantityTakeoffTests(ITestOutputHelper output) => _out = output;

    private const double Yd3PerFt3 = 1.0 / 27.0;

    // One storey, ten feet high, on a ten-foot square grid. Joints P1..P4 bound a 120 in square;
    // P5..P8 bound a 60 in square inside it; P9/P10 are a wall's two ends; P11 is a column.
    private static E2kDocument Model(params string[] body) => E2kDocument.Parse(new[]
    {
        "$ CONTROLS",
        "  UNITS  \"Kip\"  \"in\"",
        "",
        "$ STORIES - IN SEQUENCE FROM TOP",
        "  STORY \"LEVEL 1\"  HEIGHT 120",
        "  STORY \"Base\"  ELEV 0",
        "",
        "$ MATERIAL PROPERTIES",
        "  MATERIAL  \"30 MPa Floor\"  TYPE \"Concrete\"",
        "",
        "$ SLAB PROPERTIES",
        "  SHELLPROP  \"KOR-S12\"  PROPTYPE  \"Slab\"  MATERIAL \"30 MPa Floor\"  SLABTYPE \"Slab\"  SLABTHICKNESS 12",
        "",
        "$ WALL PROPERTIES",
        "  SHELLPROP  \"KOR-W12\"  PROPTYPE  \"Wall\"  MATERIAL \"65 MPa Walls\"  WALLTHICKNESS 12",
        "",
        "$ FRAME SECTIONS",
        "  FRAMESECTION  \"KOR-C12x12\"  MATERIAL \"65 MPa Columns\"  SHAPE \"Concrete Rectangular\"  D 12 B 12 ",
        "  FRAMESECTION  \"KOR-D12\"  MATERIAL \"65 MPa Columns\"  SHAPE \"Concrete Circle\"  D 12 ",
        "  FRAMESECTION  \"HSS 8x8x0.375\"  MATERIAL \"Steel ASTM A500\"  SHAPE \"HSS 8x8x0.375\"  FILE \"AISC14\" ",
        "",
        "$ POINT COORDINATES",
        "  POINT \"P1\"  0 0 0",
        "  POINT \"P2\"  120 0 0",
        "  POINT \"P3\"  120 120 0",
        "  POINT \"P4\"  0 120 0",
        "  POINT \"P5\"  30 30 0",
        "  POINT \"P6\"  90 30 0",
        "  POINT \"P7\"  90 90 0",
        "  POINT \"P8\"  30 90 0",
        "  POINT \"P9\"  0 0 0",
        "  POINT \"P10\"  120 0 0",
        "  POINT \"P11\"  60 60 0",
        "  POINT \"P12\"  1000 1000 0",
        "  POINT \"P13\"  1060 1000 0",
        "  POINT \"P14\"  1060 1060 0",
        "  POINT \"P15\"  1000 1060 0",
        "",
    }.Concat(body).Concat(new[] { "", "$ END OF MODEL FILE" }).ToArray());

    private static string[] Slab => new[]
    {
        "$ AREA CONNECTIVITIES",
        "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
        "",
        "$ AREA ASSIGNS",
        "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"  CARDINALPOINT \"MIDDLE\"",
    };

    // ---------------------------------------------------------------------------------------
    // A slab is the area the drawings enclose times the thickness they state.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ASlabIsItsEnclosedAreaTimesItsStatedThickness()
    {
        var result = E2kQuantityTakeoff.Read(Model(Slab));

        var slab = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Slab);

        // 10 ft x 10 ft x 12 in = 100 ft³
        Assert.Equal(100.0 * Yd3PerFt3, slab.ConcreteVolume, 6);

        // Soffit 100 ft² + edge 40 ft of perimeter x 1 ft = 140 ft²
        Assert.Equal(140.0, slab.FormworkArea, 6);

        Assert.Equal("LEVEL 1", slab.Level);
        Assert.Equal("30 MPa", slab.Grade);
    }

    [Fact]
    public void AHoleCutInASlabIsNotPricedAsConcrete()
    {
        var result = E2kQuantityTakeoff.Read(Model(
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "  AREA \"O1\"  AREA  4  \"P5\"  \"P6\"  \"P7\"  \"P8\"  0  0  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
            "  AREAASSIGN  \"O1\"  \"LEVEL 1\"  OPENING \"Yes\""));

        var slab = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Slab);

        // 100 ft² less a 5 ft x 5 ft hole = 75 ft², x 1 ft = 75 ft³
        Assert.Equal(75.0 * Yd3PerFt3, slab.ConcreteVolume, 6);
        Assert.Equal(25.0, result.OpeningAreaDeducted, 6);
    }

    // ---------------------------------------------------------------------------------------
    // Found by the adversarial audit, 2026-08-27. Each of these produced a plausible wrong number.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AMetricModelIsPricedInItsOwnUnitsNotAsThoughMillimetresWereInches()
    {
        // SLABTHICKNESS 300 in a millimetre model is 300 mm. Read as 300 INCHES while the plan
        // coordinates were correctly scaled, a 10 m x 10 m x 300 mm slab priced at about 762 m³
        // instead of 30 — twenty-five times over, and no flag fired because MM is a unit we know.
        var model = E2kDocument.Parse(new[]
        {
            "$ CONTROLS",
            "  UNITS  \"kN\"  \"MM\"",
            "",
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 1\"  HEIGHT 3000",
            "  STORY \"Base\"  ELEV 0",
            "",
            "$ SLAB PROPERTIES",
            "  SHELLPROP  \"S300\"  PROPTYPE  \"Slab\"  MATERIAL \"30 MPa\"  SLABTHICKNESS 300",
            "",
            "$ POINT COORDINATES",
            "  POINT \"P1\"  0 0 0",
            "  POINT \"P2\"  10000 0 0",
            "  POINT \"P3\"  10000 10000 0",
            "  POINT \"P4\"  0 10000 0",
            "",
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"S300\"",
            "",
            "$ END OF MODEL FILE",
        });

        var slab = Assert.Single(E2kQuantityTakeoff.Read(model, UnitSystem.Metric).Inputs);

        // 10 m x 10 m x 0.3 m = 30 m³
        Assert.Equal(30.0, slab.ConcreteVolume, 3);
    }

    [Fact]
    public void AMetricColumnSectionIsPricedInItsOwnUnitsToo()
    {
        var model = E2kDocument.Parse(new[]
        {
            "$ CONTROLS",
            "  UNITS  \"kN\"  \"MM\"",
            "",
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 1\"  HEIGHT 3000",
            "  STORY \"Base\"  ELEV 0",
            "",
            "$ FRAME SECTIONS",
            "  FRAMESECTION  \"C500\"  MATERIAL \"65 MPa\"  SHAPE \"Concrete Rectangular\"  D 500 B 500 ",
            "",
            "$ POINT COORDINATES",
            "  POINT \"P1\"  0 0 0",
            "",
            "$ LINE CONNECTIVITIES",
            "  LINE  \"C1\"  COLUMN  \"P1\"  \"P1\"  1",
            "",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"C1\"  \"LEVEL 1\"  SECTION \"C500\"  ANG 0",
            "",
            "$ END OF MODEL FILE",
        });

        var col = Assert.Single(E2kQuantityTakeoff.Read(model, UnitSystem.Metric).Inputs);

        // 0.5 x 0.5 x 3.0 = 0.75 m³
        Assert.Equal(0.75, col.ConcreteVolume, 4);
    }

    [Fact]
    public void AHoleIsDeductedFromOneSlabEvenWhenTwoOverlapOnAStorey()
    {
        // Deducting from every plate containing the hole's centre counted it twice.
        var result = E2kQuantityTakeoff.Read(Model(
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "  AREA \"F2\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "  AREA \"O1\"  AREA  4  \"P5\"  \"P6\"  \"P7\"  \"P8\"  0  0  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
            "  AREAASSIGN  \"F2\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
            "  AREAASSIGN  \"O1\"  \"LEVEL 1\"  OPENING \"Yes\""));

        // Two 100 ft² slabs, one 25 ft² hole taken off ONE of them: 175 ft³, not 150.
        Assert.Equal(175.0 * Yd3PerFt3, result.Inputs.Sum(i => i.ConcreteVolume), 6);
        Assert.Equal(25.0, result.OpeningAreaDeducted, 6);
    }

    [Fact]
    public void AHoleBiggerThanEverySlabOnItsStoreyIsReportedNotClampedToZero()
    {
        var result = E2kQuantityTakeoff.Read(Model(
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P5\"  \"P6\"  \"P7\"  \"P8\"  0  0  0  0",
            "  AREA \"O1\"  AREA  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
            "  AREAASSIGN  \"O1\"  \"LEVEL 1\"  OPENING \"Yes\""));

        // The slab keeps its full 25 ft²; the oversized hole is reported, not silently applied.
        var slab = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Slab);
        Assert.Equal(25.0 * Yd3PerFt3, slab.ConcreteVolume, 6);

        var orphan = Assert.Single(result.Residual, r => r.Object == "O1");
        Assert.Contains("resolved to no slab", orphan.Note);
    }

    [Fact]
    public void OneMemberWrittenTwiceOnOneStoreyIsPouredOnce()
    {
        // The DXF publisher drops these, but this reader takes any finished model.
        var result = E2kQuantityTakeoff.Read(Model(
            "$ AREA CONNECTIVITIES",
            "  AREA \"W1\"  PANEL  4  \"P9\"  \"P10\"  \"P10\"  \"P9\"  1  1  0  0",
            "",
            "$ LINE CONNECTIVITIES",
            "  LINE  \"C1\"  COLUMN  \"P11\"  \"P11\"  1",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"W1\"  \"LEVEL 1\"  SECTION \"KOR-W12\"",
            "  AREAASSIGN  \"W1\"  \"LEVEL 1\"  SECTION \"KOR-W12\"",
            "",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"C1\"  \"LEVEL 1\"  SECTION \"KOR-C12x12\"  ANG 0",
            "  LINEASSIGN  \"C1\"  \"LEVEL 1\"  SECTION \"KOR-C12x12\"  ANG 0"));

        Assert.Equal(2, result.MembersRead);
        Assert.Equal(110.0 * Yd3PerFt3, result.Inputs.Sum(i => i.ConcreteVolume), 6);
    }

    [Fact]
    public void AWallFoldedRoundACornerIsMeasuredAlongItselfNotAcrossTheDiagonal()
    {
        // First-to-last distance on a three-point panel is the diagonal: 14.1 ft instead of 20.
        var result = E2kQuantityTakeoff.Read(Model(
            "$ AREA CONNECTIVITIES",
            "  AREA \"W1\"  PANEL  6  \"P1\"  \"P2\"  \"P3\"  \"P3\"  \"P2\"  \"P1\"  1  1  1  0  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"W1\"  \"LEVEL 1\"  SECTION \"KOR-W12\""));

        var wall = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Wall);

        // P1(0,0) -> P2(120,0) -> P3(120,120) is 20 ft of wall, 10 ft high, 12 in thick = 200 ft³.
        Assert.Equal(200.0 * Yd3PerFt3, wall.ConcreteVolume, 6);
    }

    [Fact]
    public void AnOpeningOverNoSlabIsReportedRatherThanDeductedTwice()
    {
        // The hole sits a thousand feet away from the only floor in the model.
        var result = E2kQuantityTakeoff.Read(Model(
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "  AREA \"O1\"  AREA  4  \"P12\"  \"P13\"  \"P14\"  \"P15\"  0  0  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
            "  AREAASSIGN  \"O1\"  \"LEVEL 1\"  OPENING \"Yes\""));

        var slab = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Slab);
        Assert.Equal(100.0 * Yd3PerFt3, slab.ConcreteVolume, 6);
        Assert.Equal(0.0, result.OpeningAreaDeducted, 6);

        var orphan = Assert.Single(result.Residual, r => r.Object == "O1");
        Assert.Contains("resolved to no slab", orphan.Note);
    }

    // ---------------------------------------------------------------------------------------
    // A wall runs the storey it rises through.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AWallIsItsPlanLengthTimesTheStoreyItRisesThrough()
    {
        var result = E2kQuantityTakeoff.Read(Model(
            "$ AREA CONNECTIVITIES",
            "  AREA \"W1\"  PANEL  4  \"P9\"  \"P10\"  \"P10\"  \"P9\"  1  1  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"W1\"  \"LEVEL 1\"  SECTION \"KOR-W12\"  PIER \"PIER1\""));

        var wall = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Wall);

        // 10 ft long x 10 ft storey x 12 in = 100 ft³
        Assert.Equal(100.0 * Yd3PerFt3, wall.ConcreteVolume, 6);

        // Both faces: 2 x 10 x 10 = 200 ft²
        Assert.Equal(200.0, wall.FormworkArea, 6);
    }

    [Fact]
    public void AWallOnASiteModelRisesFromTheFloorBelowNotFromItsOwnTwin()
    {
        // A site model names one physical floor once per building, a few inches apart, in ONE
        // global storey list. ETABS's HEIGHT field is then the gap to the neighbour in that list —
        // five and a half inches on the published 31168 site model — and a wall priced off it came
        // to 6 cubic yards where the one-building file said 236 for the same wall.
        var model = E2kDocument.Parse(new[]
        {
            "$ CONTROLS",
            "  UNITS  \"Kip\"  \"in\"",
            "",
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"C-LEVEL 4\"  HEIGHT 5.5",     // building C's floor, 5.5 in above the tower's
            "  STORY \"LEVEL 4\"  HEIGHT 114.5",     // the tower's floor at the same level
            "  STORY \"C-LEVEL 3\"  HEIGHT 5.5",
            "  STORY \"LEVEL 3\"  HEIGHT 114.5",
            "  STORY \"Base\"  ELEV 0",
            "",
            "$ WALL PROPERTIES",
            "  SHELLPROP  \"KOR-W12\"  PROPTYPE  \"Wall\"  MATERIAL \"65 MPa Walls\"  WALLTHICKNESS 12",
            "",
            "$ POINT COORDINATES",
            "  POINT \"P9\"  0 0 0",
            "  POINT \"P10\"  120 0 0",
            "",
            "$ AREA CONNECTIVITIES",
            "  AREA \"W1\"  PANEL  4  \"P9\"  \"P10\"  \"P10\"  \"P9\"  1  1  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"W1\"  \"C-LEVEL 4\"  SECTION \"KOR-W12\"",
            "",
            "$ END OF MODEL FILE",
        });

        var wall = Assert.Single(E2kQuantityTakeoff.Read(model).Inputs, i => i.Element == TakeoffElementType.Wall);

        // C-LEVEL 4 stands at 240.0, C-LEVEL 3 at 120.0 — a 120 in storey, not the 5.5 in gap to
        // LEVEL 4 directly beneath it. 10 ft long x 10 ft x 12 in = 100 ft³.
        Assert.Equal(100.0 * Yd3PerFt3, wall.ConcreteVolume, 6);
    }

    [Fact]
    public void APierLabelIsNotEvidenceOfAShearWall()
    {
        // Our own generator gives every wall a pier. Reading that back as "shear" would inflate
        // wall steel by three quarters on every model we produce.
        var result = E2kQuantityTakeoff.Read(Model(
            "$ AREA CONNECTIVITIES",
            "  AREA \"W1\"  PANEL  4  \"P9\"  \"P10\"  \"P10\"  \"P9\"  1  1  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"W1\"  \"LEVEL 1\"  SECTION \"KOR-W12\"  PIER \"PIER1\""));

        var wall = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Wall);
        Assert.Null(wall.Variant);
        Assert.Contains(result.Flags, f => f.Code == "WALL_VARIANT_UNSPLIT");
    }

    // ---------------------------------------------------------------------------------------
    // A column is its section times the storey it rises through.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ARectangularColumnIsItsSectionTimesTheStorey()
    {
        var result = E2kQuantityTakeoff.Read(Model(
            "$ LINE CONNECTIVITIES",
            "  LINE  \"C1\"  COLUMN  \"P11\"  \"P11\"  1",
            "",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"C1\"  \"LEVEL 1\"  SECTION \"KOR-C12x12\"  ANG 0"));

        var col = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Column);

        // 1 ft² x 10 ft = 10 ft³
        Assert.Equal(10.0 * Yd3PerFt3, col.ConcreteVolume, 6);

        // Perimeter 4 ft x 10 ft = 40 ft²
        Assert.Equal(40.0, col.FormworkArea, 6);
        Assert.Equal("65 MPa", col.Grade);
    }

    [Fact]
    public void ARoundColumnIsPricedRoundNotSquare()
    {
        var result = E2kQuantityTakeoff.Read(Model(
            "$ LINE CONNECTIVITIES",
            "  LINE  \"C1\"  COLUMN  \"P11\"  \"P11\"  1",
            "",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"C1\"  \"LEVEL 1\"  SECTION \"KOR-D12\"  ANG 0"));

        var col = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Column);

        // pi/4 x 1 ft² x 10 ft
        Assert.Equal(Math.PI / 4.0 * 10.0 * Yd3PerFt3, col.ConcreteVolume, 6);
    }

    [Fact]
    public void ASteelColumnCarriesNoConcreteAndIsSaidSo()
    {
        var result = E2kQuantityTakeoff.Read(Model(
            "$ LINE CONNECTIVITIES",
            "  LINE  \"C1\"  COLUMN  \"P11\"  \"P11\"  1",
            "",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"C1\"  \"LEVEL 1\"  SECTION \"HSS 8x8x0.375\"  ANG 0"));

        Assert.DoesNotContain(result.Inputs, i => i.Element == TakeoffElementType.Column);
        var left = Assert.Single(result.Residual, r => r.Object == "C1");
        Assert.Contains("steel section carries no concrete", left.Note);
    }

    // ---------------------------------------------------------------------------------------
    // What the numbers rest on is printed, not assumed silently. Doctrine gate 3.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void PricingAMemberOverAWholeStoreySaysThatItDidSo()
    {
        var result = E2kQuantityTakeoff.Read(Model(
            "$ LINE CONNECTIVITIES",
            "  LINE  \"C1\"  COLUMN  \"P11\"  \"P11\"  1",
            "",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"C1\"  \"LEVEL 1\"  SECTION \"KOR-C12x12\"  ANG 0"));

        Assert.Contains(result.Flags, f => f.Code == "HEIGHT_FROM_STOREY");
    }

    [Fact]
    public void AStoreyWhoseOccupancyTheModelDoesNotStateTakesNoVariantAndSaysWhich()
    {
        var result = E2kQuantityTakeoff.Read(Model(Slab));

        var slab = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Slab);
        Assert.Null(slab.Variant);

        var flag = Assert.Single(result.Flags, f => f.Code == "SLAB_VARIANT_DEFAULTED");
        Assert.Contains("LEVEL 1", flag.Note);
    }

    [Fact]
    public void TheStoreyVocabularyIsTheFirmsNotThisToolsAndItSetsTheVariant()
    {
        // The same word lists the model was built with — dxf.roof-words, dxf.parkade-words.
        var model = E2kDocument.Parse(new[]
        {
            "$ CONTROLS",
            "  UNITS  \"Kip\"  \"in\"",
            "",
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"PODIUM ROOF\"  HEIGHT 120",
            "  STORY \"Base\"  ELEV 0",
            "",
            "$ SLAB PROPERTIES",
            "  SHELLPROP  \"KOR-S12\"  PROPTYPE  \"Slab\"  MATERIAL \"30 MPa Floor\"  SLABTHICKNESS 12",
            "",
            "$ POINT COORDINATES",
            "  POINT \"P1\"  0 0 0",
            "  POINT \"P2\"  120 0 0",
            "  POINT \"P3\"  120 120 0",
            "  POINT \"P4\"  0 120 0",
            "",
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"PODIUM ROOF\"  SECTION \"KOR-S12\"",
            "",
            "$ END OF MODEL FILE",
        });

        var result = E2kQuantityTakeoff.Read(model, UnitSystem.Imperial, roofWords: new[] { "ROOF" }, parkadeWords: new[] { "PARKADE" });

        var slab = Assert.Single(result.Inputs, i => i.Element == TakeoffElementType.Slab);
        Assert.Equal("roof", slab.Variant);
    }

    [Fact]
    public void FoundationsAreNamedAsMissingBecauseTheyAreNotInASuperstructureModel()
    {
        var result = E2kQuantityTakeoff.Read(Model(Slab));

        var foundation = Assert.Single(result.Residual, r => r.Kind == "foundation");
        Assert.Contains("Quantify them by hand", foundation.Note);
    }

    [Fact]
    public void AModelThatDeclaresNoUnitSaysSoRatherThanPricingItSilently()
    {
        var model = E2kDocument.Parse(new[]
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 1\"  HEIGHT 120",
            "",
            "$ SLAB PROPERTIES",
            "  SHELLPROP  \"KOR-S12\"  PROPTYPE  \"Slab\"  MATERIAL \"30 MPa Floor\"  SLABTHICKNESS 12",
            "",
            "$ POINT COORDINATES",
            "  POINT \"P1\"  0 0 0",
            "  POINT \"P2\"  120 0 0",
            "  POINT \"P3\"  120 120 0",
            "  POINT \"P4\"  0 120 0",
            "",
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
            "",
            "$ END OF MODEL FILE",
        });

        var result = E2kQuantityTakeoff.Read(model);
        Assert.Contains(result.Flags, f => f.Code == "UNIT_ASSUMED");
    }

    // ---------------------------------------------------------------------------------------
    // The whole point: the takeoff and the model can never disagree about the building.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void EveryPricedObjectIsAnObjectTheModelCarries()
    {
        var doc = Model(new[]
        {
            "$ AREA CONNECTIVITIES",
            "  AREA \"F1\"  FLOOR  4  \"P1\"  \"P2\"  \"P3\"  \"P4\"  0  0  0  0",
            "  AREA \"W1\"  PANEL  4  \"P9\"  \"P10\"  \"P10\"  \"P9\"  1  1  0  0",
            "",
            "$ LINE CONNECTIVITIES",
            "  LINE  \"C1\"  COLUMN  \"P11\"  \"P11\"  1",
            "",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 1\"  SECTION \"KOR-S12\"",
            "  AREAASSIGN  \"W1\"  \"LEVEL 1\"  SECTION \"KOR-W12\"",
            "",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"C1\"  \"LEVEL 1\"  SECTION \"KOR-C12x12\"  ANG 0",
        }.ToArray());

        var result = E2kQuantityTakeoff.Read(doc);

        Assert.Equal(3, result.MembersRead);
        var storeys = doc.ReadStories().Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(result.Inputs, i => Assert.Contains(i.Level, storeys));

        _out.WriteLine($"slab+wall+column = {result.Inputs.Sum(i => i.ConcreteVolume) * 27:N1} ft³");

        // 100 + 100 + 10 = 210 ft³
        Assert.Equal(210.0 * Yd3PerFt3, result.Inputs.Sum(i => i.ConcreteVolume), 6);
    }
}
