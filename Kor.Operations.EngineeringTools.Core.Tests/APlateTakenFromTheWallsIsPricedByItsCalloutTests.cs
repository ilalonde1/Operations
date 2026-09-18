using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A PLATE TAKEN FROM THE WALLS IS PRICED BY THE CALLOUT INSIDE IT (intake step 120, 2026-09-18). The callout pricing
/// ran once, over the slab-edge rings, and the later rules that make a plate where no slab edge closes - the floor
/// taken from the perimeter wall's ring, the panel fill - ran after it: their plates went to the model at the 12-in
/// default with the thickness printed inside them twelve times. 31087's P1 («10" SLAB» × 12) was one of 52 of its 61
/// plates so priced; run 38's section 8 counted 12/10 × 33 and 12/8 × 26 across the corpus. WHAT THIS COVERS: a
/// perimeter wall's ring with no slab edge and one callout inside it, priced. WHAT IT DOES NOT: the slab-edge plates
/// (PlateThicknessFromTheDrawingTests), two callouts in one plate (the same tests), a callout outside every plate.
/// </summary>
public sealed class APlateTakenFromTheWallsIsPricedByItsCalloutTests
{
    private static IEnumerable<DxfSegment> Ring(string layer, double x0, double y0, double x1, double y1)
    {
        var c = new[] { new DxfPoint(x0, y0), new DxfPoint(x1, y0), new DxfPoint(x1, y1), new DxfPoint(x0, y1) };
        for (int i = 0; i < 4; i++) yield return new DxfSegment(layer, c[i], c[(i + 1) % 4]) { OfClosedOutline = true };
    }

    [Fact]
    public void TheWallsRingWithTheCalloutInsideItIsThatThick()
    {
        // a 100 x 75 ft basement: the perimeter wall as its two faces 12 in apart, no slab edge drawn, «10" SLAB» inside
        var set = StructuralPlanClassifier.Classify(
            Ring("JBP_B_WALL", 0, 0, 1200, 900).Concat(Ring("JBP_B_WALL", 12, 12, 1188, 888)),
            tags: [new DxfPositionedTag("10\" SLAB", new DxfPoint(600, 450), "TEXT", "10\" SLAB")]);

        var plate = Assert.Single(set.Slabs);
        Assert.True(plate.Area > 57600, $"the walls' ring should be the floor, got {plate.Area:0} sq in on {plate.Layer}");
        Assert.Equal(10, plate.ThicknessInchesFromTag);
        Assert.Contains(set.Flags, f => f.Contains("is 10\" thick", StringComparison.Ordinal));
    }
}
