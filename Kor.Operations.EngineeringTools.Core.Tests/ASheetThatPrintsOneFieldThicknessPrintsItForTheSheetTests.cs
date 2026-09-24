using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A SHEET THAT PRINTS ONE FIELD THICKNESS PRINTS IT FOR THE SHEET (intake step 140, 2026-09-23).
///
/// <see cref="PlateThicknessFromTheDrawingTests"/> banks the rule this extends: the thickness printed INSIDE a
/// plate is that plate's, and a thicker region drawn inside a floor takes its own call-out. That pass asks only
/// what is inside. A plate with nothing inside it falls all the way through to the engineer's 12" default.
///
/// MEASURED OVER THE BUILT CORPUS BEFORE THIS WAS WRITTEN, which is what makes it a rule rather than a guess:
///
///   1,586 of 2,763 floor plates — 57% — carry the 12" default.
///   112 of 112 sets that default a plate print thickness call-outs on their own sheets. NOT ONE of the 1,586
///   is in a set whose drawings are silent. "The drawing does not say" was never the reason.
///   Of the 1,433 plan sheets that produced a plate: 456 print no field call-out at all; 448 print exactly one
///   and a plate took it; 46 print exactly one and NO plate took it; 414 print several and a plate took one;
///   69 print several and none was taken.
///
/// Those 46 are this rule's whole target. 01379-01 does it on eleven OVERALL PLAN sheets, each printing
/// "8&quot; SLAB" once, outside every closed outline. A sheet with ONE field number has given its answer.
///
/// WHAT THIS COVERS: a plate that claimed NO call-out of its own, on a sheet whose field call-outs — those at
/// or under <see cref="QuantityTakeoff.SlabThicknessCallout.ZonerFieldMaxIn"/> — are all the same number. The
/// flag says the number was printed OUTSIDE the plate, so a reader can tell it from a plate's own reading.
///
/// WHAT IT DOES NOT: a sheet printing two different field thicknesses says nothing here and its silent plates
/// keep the default — deliberately, because choosing between them is the ambiguity the pass above already
/// refuses. A plate the pass above REFUSED for carrying two numbers keeps that refusal and its flag; this never
/// overrides a drawing that contradicts itself. A mat or transfer slab is not collected at all, so a sheet
/// printing "8&quot; SLAB" and "56&quot; MAT" still counts as printing one field thickness — which is the
/// intent, and also the thing to watch. And it cannot help the 456 sheets that print nothing: those need the
/// storey or the set, which this rule does not reach for.
///
/// A SAME-CLASS FAULT IT WOULD NOT CATCH: a sheet that prints one field thickness for its MAIN floor and
/// carries a second, unlabelled plate that is genuinely a different thickness — a podium terrace at 6" under a
/// tower at 8". One number, two floors, and this gives both the same.
/// </summary>
public sealed class ASheetThatPrintsOneFieldThicknessPrintsItForTheSheetTests
{
    private readonly ITestOutputHelper _out;
    public ASheetThatPrintsOneFieldThicknessPrintsItForTheSheetTests(ITestOutputHelper output) => _out = output;

    private const string SlabLayer = "JBP_C_SLABEDG";

    private static IEnumerable<DxfSegment> Ring(double x0, double y0, double x1, double y1)
    {
        yield return new DxfSegment(SlabLayer, new DxfPoint(x0, y0), new DxfPoint(x1, y0));
        yield return new DxfSegment(SlabLayer, new DxfPoint(x1, y0), new DxfPoint(x1, y1));
        yield return new DxfSegment(SlabLayer, new DxfPoint(x1, y1), new DxfPoint(x0, y1));
        yield return new DxfSegment(SlabLayer, new DxfPoint(x0, y1), new DxfPoint(x0, y0));
    }

    private static PlanClassificationOptions Options() => new()
    {
        SlabLayerPatterns = [SlabLayer],
        WallLayerPatterns = ["JBP_V-WALL"],
        ColumnLayerPatterns = ["JBP_V_COL"],
    };

    private static DxfPositionedTag Tag(string text, double x, double y)
        => new(text, new DxfPoint(x, y), "JBP_TAG_FLOORS", text);

    /// <summary>01379-01's shape: one ring, and the sheet's only call-out standing outside it.</summary>
    private static PlanGeometrySet OneRingOneCalloutOutsideIt(params DxfPositionedTag[] tags)
        => StructuralPlanClassifier.Classify(Ring(0, 0, 1200, 900).ToList(), Options(), sheet: null, tags: tags);

    [Fact]
    public void APlateWithNothingInsideItTakesTheSheetsOnlyFieldThickness()
    {
        var set = OneRingOneCalloutOutsideIt(Tag("8\" SLAB", 3000, 3000));   // well outside the ring
        var plate = Assert.Single(set.Slabs);
        foreach (string f in set.Flags) _out.WriteLine(f);

        Assert.Equal(8, plate.ThicknessInchesFromTag);
        Assert.Contains(set.Flags, f => f.Contains("the only field thickness this sheet prints", StringComparison.Ordinal));
        Assert.Contains(set.Flags, f => f.Contains("Printed OUTSIDE this plate", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePlatesOwnCalloutStillWinsOverTheSheets()
    {
        // the pass this extends is untouched: a number inside the plate is the plate's, and the flag says so
        var set = OneRingOneCalloutOutsideIt(Tag("10\" SLAB", 600, 450), Tag("10\" SLAB", 3000, 3000));
        var plate = Assert.Single(set.Slabs);
        Assert.Equal(10, plate.ThicknessInchesFromTag);
        Assert.Contains(set.Flags, f => f.Contains("printed inside it", StringComparison.Ordinal));
        Assert.DoesNotContain(set.Flags, f => f.Contains("the only field thickness", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoDifferentFieldThicknessesOnTheSheetSayNothingAndThePlateKeepsTheDefault()
    {
        var set = OneRingOneCalloutOutsideIt(Tag("8\" SLAB", 3000, 3000), Tag("10\" SLAB", 3000, 3400));
        var plate = Assert.Single(set.Slabs);
        Assert.Null(plate.ThicknessInchesFromTag);
        Assert.DoesNotContain(set.Flags, f => f.Contains("the only field thickness", StringComparison.Ordinal));
    }

    [Fact]
    public void AMatIsNotTheSheetsFieldThicknessAndDoesNotSpoilItsOneFieldNumber()
    {
        // "56\" MAT"-shaped call-out: above the field maximum, so it is not collected and the 8" still stands alone
        var set = OneRingOneCalloutOutsideIt(Tag("8\" SLAB", 3000, 3000), Tag("56\" SLAB", 3000, 3400));
        var plate = Assert.Single(set.Slabs);
        Assert.Equal(8, plate.ThicknessInchesFromTag);
    }

    [Fact]
    public void ASheetWithNoCalloutAtAllIsUntouched()
    {
        var set = StructuralPlanClassifier.Classify(Ring(0, 0, 1200, 900).ToList(), Options(), sheet: null,
            tags: [Tag("LEVEL 4", 3000, 3000)]);
        var plate = Assert.Single(set.Slabs);
        Assert.Null(plate.ThicknessInchesFromTag);
    }

    [Fact]
    public void TheKnobPutsTheOldBehaviourBack()
    {
        Assert.Equal(8, Assert.Single(OneRingOneCalloutOutsideIt(Tag("8\" SLAB", 3000, 3000)).Slabs).ThicknessInchesFromTag);
        string? was = Environment.GetEnvironmentVariable("KOR_STEP140_OFF");
        try
        {
            Environment.SetEnvironmentVariable("KOR_STEP140_OFF", "1");
            Assert.Null(Assert.Single(OneRingOneCalloutOutsideIt(Tag("8\" SLAB", 3000, 3000)).Slabs).ThicknessInchesFromTag);
        }
        finally { Environment.SetEnvironmentVariable("KOR_STEP140_OFF", was); }
    }
}
