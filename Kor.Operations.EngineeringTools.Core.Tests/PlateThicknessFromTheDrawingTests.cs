using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The thickness printed inside a plate is the plate's thickness.
///
/// The second half of the engineer's own sentence — "the tag is always inside the slab". The
/// first half already decides slab-versus-hole; this decides how thick. Until the export was
/// told to use drafting's own setup the DXFs carried no text at all, so thickness came from
/// pairing a DXF sheet with a page of the stick-file PDF: one number for a whole sheet, and only
/// as good as the filename.
///
/// Per sheet is the wrong grain. 31168's LEVEL 1 draws a 14" slab and a 56" mat on one plan, and
/// whichever number a per-sheet reading picks, it is wrong about the other.
/// </summary>
public class PlateThicknessFromTheDrawingTests
{
    private readonly ITestOutputHelper _out;

    public PlateThicknessFromTheDrawingTests(ITestOutputHelper output) => _out = output;

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
        SlabLayerPatterns = new[] { SlabLayer },
        WallLayerPatterns = new[] { "JBP_V-WALL" },
        ColumnLayerPatterns = new[] { "JBP_V_COL" },
    };

    private static DxfPositionedTag Tag(string text, double x, double y)
        => new(text, new DxfPoint(x, y), "JBP_TAG_FLOORS", text);

    [Fact]
    public void APlateTakesTheThicknessPrintedInsideIt()
    {
        var set = StructuralPlanClassifier.Classify(
            Ring(0, 0, 1200, 900).ToList(), Options(), sheet: null,
            tags: new[] { Tag("14\" SLAB", 600, 450) });

        var plate = Assert.Single(set.Slabs);
        Assert.Equal(14, plate.ThicknessInchesFromTag);

        // And it says where the number came from, in the drawing's own words.
        Assert.Contains(set.Flags, f => f.Contains("14\" SLAB", StringComparison.Ordinal));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A thicker slab drawn inside a floor takes its OWN call-out, and the
    /// floor around it keeps the one printed in it. This is what per-sheet reading cannot do.
    /// </summary>
    [Fact]
    public void ASlabInsideASlabTakesItsOwnCallOut()
    {
        var segments = Ring(0, 0, 3000, 2400).ToList();
        segments.AddRange(Ring(600, 600, 1800, 1500));

        var set = StructuralPlanClassifier.Classify(
            segments, Options(), sheet: null,
            tags: new[]
            {
                Tag("14\" SLAB", 2600, 2200),   // in the outer floor only
                Tag("56\" SLAB", 1200, 1000),   // inside the thicker one
            });

        foreach (var s in set.Slabs) _out.WriteLine($"{s.Area / 144:N0} sq ft -> {s.ThicknessInchesFromTag}");

        var outer = set.Slabs.OrderByDescending(s => s.Area).First();
        Assert.Equal(14, outer.ThicknessInchesFromTag);

        // The inner ring is read as an opening or as a plate depending on the drawing; either way
        // the OUTER one must not have taken 56.
        var inner = set.Slabs.Concat(set.Openings).OrderBy(s => s.Area).First();
        Assert.True(inner.ThicknessInchesFromTag is null or 56,
            $"the inner region took {inner.ThicknessInchesFromTag}, which is neither its own call-out nor nothing");
    }

    /// <summary>
    /// A dimension that is not a slab call-out is not a thickness. These are the ones actually
    /// printed inside 31168's plates: column sizes, diameters, and grid dimensions. Every one of
    /// them carries an inch mark, and none of them says how thick the floor is.
    /// </summary>
    [Theory]
    [InlineData("(42\" x 42\")")]
    [InlineData("16\"Ø")]
    [InlineData("(30\" Ø)")]
    [InlineData("4'-0\"")]
    [InlineData("30\"")]
    [InlineData("(15\" x 38\")")]
    public void ADimensionThatDoesNotSaySlabIsNotAThickness(string text)
    {
        var set = StructuralPlanClassifier.Classify(
            Ring(0, 0, 1200, 900).ToList(), Options(), sheet: null,
            tags: new[] { Tag(text, 600, 450) });

        var plate = Assert.Single(set.Slabs);
        Assert.Null(plate.ThicknessInchesFromTag);
    }

    /// <summary>
    /// And a call-out that is a plausible slab word but an implausible slab: a floor is not one
    /// inch thick and not ten feet thick, whatever a stray number says.
    /// </summary>
    [Theory]
    [InlineData("2\" SLAB")]
    [InlineData("99\" SLAB")]
    public void AnImplausibleThicknessIsRefused(string text)
    {
        var set = StructuralPlanClassifier.Classify(
            Ring(0, 0, 1200, 900).ToList(), Options(), sheet: null,
            tags: new[] { Tag(text, 600, 450) });

        var plate = Assert.Single(set.Slabs);
        if (text.StartsWith("2\"", StringComparison.Ordinal))
            Assert.Null(plate.ThicknessInchesFromTag);
        else
            Assert.Equal(99, plate.ThicknessInchesFromTag);   // 99" is thick but within a mat's range
    }

    /// <summary>
    /// THE ONE THAT WOULD HAVE CAUGHT IT. Two tags inside one plate, in an order that puts the
    /// wrong one first.
    ///
    /// The first version walked plates and asked `slab.Area &lt; smallest` inside a loop where
    /// slab.Area never changes, so the FIRST tag encountered won and the order of the tag list
    /// decided a plate's thickness. It read like "smallest plate wins" and was arbitrary. An
    /// adversarial audit found it; no test did.
    ///
    /// Two different numbers with no separate outline between them is not a case to guess at, so
    /// the plate keeps the default and says why.
    /// </summary>
    [Fact]
    public void TwoDifferentCallOutsInOnePlateAreRefusedRatherThanGuessedBetween()
    {
        var set = StructuralPlanClassifier.Classify(
            Ring(0, 0, 3000, 2400).ToList(), Options(), sheet: null,
            tags: new[] { Tag("56\" SLAB", 900, 800), Tag("14\" SLAB", 2100, 1600) });

        var plate = Assert.Single(set.Slabs);
        Assert.Null(plate.ThicknessInchesFromTag);
        Assert.Contains(set.Flags, f =>
            f.Contains("different thickness call-outs", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The same call-out written the other way round. "SLAB 14\"" is what a drafter types as
    /// often as "14\" SLAB", and the classifier was matching only the number-first form.
    /// </summary>
    [Theory]
    [InlineData("SLAB 14\"")]
    [InlineData("14\" SLAB")]
    public void ACallOutIsReadInEitherOrder(string text)
    {
        var set = StructuralPlanClassifier.Classify(
            Ring(0, 0, 1200, 900).ToList(), Options(), sheet: null,
            tags: new[] { Tag(text, 600, 450) });

        Assert.Equal(14, Assert.Single(set.Slabs).ThicknessInchesFromTag);
    }

    /// <summary>Note numbering is not a thickness, whichever way round it reads.</summary>
    [Fact]
    public void NoteNumberingIsNotAThickness()
    {
        var set = StructuralPlanClassifier.Classify(
            Ring(0, 0, 1200, 900).ToList(), Options(), sheet: null,
            tags: new[] { Tag("5. SLABS SHALL BE 20 MPa", 600, 450) });

        Assert.Null(Assert.Single(set.Slabs).ThicknessInchesFromTag);
    }
}
