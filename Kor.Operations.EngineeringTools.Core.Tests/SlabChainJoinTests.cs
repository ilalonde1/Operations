using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A slab outline closed by joining its own two loose ends recovers an edge the drawing INTERRUPTS
/// — a doorway, a beam, a wall running over it. Beyond a small part of the ring it is not
/// recovering an edge, it is drawing one.
///
/// Measured across 31168, the closures fall in two groups with nothing between 8% and 17%: 1–8%
/// where the drawing really is interrupted (61 and 62 in at a wall, 287 and 355 where a beam
/// crosses the edge) and 17–48% where the join is a slab edge the drawing never had — 2,839 in
/// across the site podium, 2,081 across level 1, and 1,044 across the mezzanine slab the engineer
/// rejected with "the slab edge is wrong".
///
/// Banked as `a-join-is-an-interruption-not-an-edge`, governed by dxf.slab-chain-join-fraction.
/// </summary>
public class SlabChainJoinTests
{
    private const string SlabLayer = "JBP_C_SLABEDG";

    private static PlanClassificationOptions Options() => new()
    {
        SlabLayerPatterns = new[] { SlabLayer },
        WallLayerPatterns = new[] { "JBP_V-WALL" },
        ColumnLayerPatterns = new[] { "JBP_V_COL" },
    };

    private static DxfPositionedTag Slab14(double x, double y)
        => new("14\" SLAB", new DxfPoint(x, y), "A-FLOR-IDEN", "14\" SLAB");

    /// <summary>A rectangle whose fourth side carries an interruption of <paramref name="gap"/>.</summary>
    private static List<DxfSegment> Rectangle(double w, double h, double gap)
        => new()
        {
            new DxfSegment(SlabLayer, new DxfPoint(0, 0), new DxfPoint(w, 0)),
            new DxfSegment(SlabLayer, new DxfPoint(w, 0), new DxfPoint(w, h)),
            new DxfSegment(SlabLayer, new DxfPoint(w, h), new DxfPoint(0, h)),
            new DxfSegment(SlabLayer, new DxfPoint(0, h), new DxfPoint(0, h / 2 + gap / 2)),
            new DxfSegment(SlabLayer, new DxfPoint(0, h / 2 - gap / 2), new DxfPoint(0, 0)),
        };

    [Fact]
    public void AnInterruptionIsClosedAndModelled()
    {
        // 120 in across a ring the drawing draws 4,080 in of: 3 per cent.
        var set = StructuralPlanClassifier.Classify(
            Rectangle(1200, 900, 120), Options(), sheet: null, tags: new[] { Slab14(600, 450) });

        var plate = Assert.Single(set.Slabs);
        Assert.True(plate.Area / 144 > 6_000, $"{plate.Area / 144:N0} sq ft");
    }

    [Fact]
    public void AJoinThatWouldBeANewEdgeIsRefusedAndNamed()
    {
        // The whole fourth side missing: 900 in against 3,300 drawn, 27 per cent — the band the
        // podium, level 1 and the mezzanine all sat in.
        var set = StructuralPlanClassifier.Classify(
            Rectangle(1200, 900, 900), Options(), sheet: null, tags: new[] { Slab14(600, 450) });

        Assert.Empty(set.Slabs);

        // Named, never swallowed: she is owed the region, the length of the join, and the reason.
        Assert.Contains(set.Flags, f =>
            f.Contains("CANDIDATE NOT MODELLED", StringComparison.Ordinal) &&
            f.Contains("loose ends", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("inventing", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The threshold is a FRACTION because the same length means different things on different
    /// rings: 355 in is an interruption on a 6,883 in ring and an invention on a 2,504 in one.
    /// </summary>
    [Fact]
    public void TheSameJoinIsJudgedAgainstTheRingItCloses()
    {
        const double gap = 300;

        var big = StructuralPlanClassifier.Classify(
            Rectangle(4800, 3600, gap), Options(), sheet: null, tags: new[] { Slab14(2400, 1800) });
        var small = StructuralPlanClassifier.Classify(
            Rectangle(600, 450, gap), Options(), sheet: null, tags: new[] { Slab14(300, 225) });

        Assert.Single(big.Slabs);       // 300 of 16,500 drawn — an interruption
        Assert.Empty(small.Slabs);      // 300 of  1,800 drawn — an edge
    }
}
