using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Recovering a slab outline the drawing leaves open — and only where the drawing says it is a
/// slab.
///
/// This mechanism shipped once without the tag test and the engineer rejected the model the next
/// morning: "on several levels (9, 3, mezz, 1) he inverted slab and opening". Closing a chain
/// recovers a REGION and says nothing about what the region is. The tag says what it is.
/// </summary>
public class TagGatedSlabRecoveryTests
{
    private const string SlabLayer = "JBP_C_SLABEDG";
    private const string ColumnLayer = "JBP_V_COL";

    /// <summary>
    /// A slab edge drawn as an open chain: three sides of a rectangle, the fourth left out the
    /// way crossing linework leaves one out on a real sheet.
    /// </summary>
    private static List<DxfSegment> OpenSlabOutline(double w = 1200, double h = 900)
        => new()
        {
            new DxfSegment(SlabLayer, new DxfPoint(0, 0), new DxfPoint(w, 0)),
            new DxfSegment(SlabLayer, new DxfPoint(w, 0), new DxfPoint(w, h)),
            new DxfSegment(SlabLayer, new DxfPoint(w, h), new DxfPoint(0, h)),
        };

    private static PlanClassificationOptions Options() => new()
    {
        SlabLayerPatterns = new[] { SlabLayer },
        ColumnLayerPatterns = new[] { ColumnLayer },
        WallLayerPatterns = new[] { "JBP_V-WALL" },
    };

    private static DxfPositionedTag Tag(string text, double x, double y)
        => new(text, new DxfPoint(x, y), "A-FLOR-IDEN", text);

    [Fact]
    public void AnOpenOutlineWithAThicknessCallOutInsideItBecomesAFloor()
    {
        var set = StructuralPlanClassifier.Classify(
            OpenSlabOutline(), Options(), sheet: null,
            tags: new[] { Tag("14\" SLAB", 600, 450) });

        var plate = Assert.Single(set.Slabs);
        Assert.True(plate.Area > 0);
        Assert.Contains(set.Flags, f =>
            f.Contains("closed by joining", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("14\" SLAB", StringComparison.Ordinal));
    }

    /// <summary>
    /// THE TEST THAT MATTERS. Without it this is the change she rejected: a recovered region
    /// with nothing to say what it is.
    /// </summary>
    [Fact]
    public void AnOpenOutlineWithNoCallOutInsideItRecoversNothing()
    {
        var set = StructuralPlanClassifier.Classify(
            OpenSlabOutline(), Options(), sheet: null, tags: System.Array.Empty<DxfPositionedTag>());

        Assert.Empty(set.Slabs);
        Assert.DoesNotContain(set.Flags, f =>
            f.Contains("closed by joining", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A call-out sitting OUTSIDE the region says nothing about it.</summary>
    [Fact]
    public void ACallOutBeyondTheOutlineDoesNotConfirmIt()
    {
        var set = StructuralPlanClassifier.Classify(
            OpenSlabOutline(), Options(), sheet: null,
            tags: new[] { Tag("14\" SLAB", 9000, 9000) });

        Assert.Empty(set.Slabs);
    }

    /// <summary>
    /// Words that are not a thickness call-out do not confirm anything either — a grid bubble
    /// or a note landing inside the region must not turn it into a floor.
    /// </summary>
    [Theory]
    [InlineData("A")]
    [InlineData("KEEP STRUC. OPEN'G")]
    [InlineData("SEE PLANS FOR SPECIAL ADDITIONAL DOWELS.")]
    public void OnlyAThicknessCallOutConfirmsAFloor(string text)
    {
        var set = StructuralPlanClassifier.Classify(
            OpenSlabOutline(), Options(), sheet: null,
            tags: new[] { Tag(text, 600, 450) });

        Assert.Empty(set.Slabs);
    }

    /// <summary>
    /// A drawing whose outline already closes is untouched by any of this — the ordinary case,
    /// and the one that must not change.
    /// </summary>
    [Fact]
    public void AClosedOutlineIsReadWithoutNeedingATag()
    {
        var closed = OpenSlabOutline();
        closed.Add(new DxfSegment(SlabLayer, new DxfPoint(0, 900), new DxfPoint(0, 0)));

        var set = StructuralPlanClassifier.Classify(
            closed, Options(), sheet: null, tags: System.Array.Empty<DxfPositionedTag>());

        Assert.Single(set.Slabs);
        Assert.DoesNotContain(set.Flags, f =>
            f.Contains("closed by joining", StringComparison.OrdinalIgnoreCase));
    }
}
