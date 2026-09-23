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

    /// <summary>An L, drawn whole: a tower floor notched round a podium, filling 50% of its own box.</summary>
    private static List<DxfSegment> Ell(double gap)
    {
        // 4,800 x 3,600 with a 3,600 x 2,400 bite out of the north-east corner: 8.64M of a 17.28M box, 50% - under
        // the 55% the shape gate asked for, and a perfectly ordinary tower floor notched round a podium
        DxfPoint[] p =
        [
            new(0, 0), new(4800, 0), new(4800, 1200), new(1200, 1200), new(1200, 3600), new(0, 3600),
        ];
        var segs = new List<DxfSegment>();
        for (int i = 0; i + 1 < p.Length; i++) segs.Add(new DxfSegment(SlabLayer, p[i], p[i + 1]));
        // the closing side, north to south, interrupted by `gap` at its middle
        segs.Add(new DxfSegment(SlabLayer, p[^1], new DxfPoint(0, 1800 + gap / 2)));
        segs.Add(new DxfSegment(SlabLayer, new DxfPoint(0, 1800 - gap / 2), p[0]));
        return segs;
    }

    /// <summary>
    /// A RING'S SHAPE DOES NOT JUDGE IT; HOW MUCH OF IT THE DRAWING DREW DOES (intake step 136, 2026-09-22).
    /// A candidate that filled under 55% of its own bounding box was refused as "a thin or hooked shape, which is
    /// what a slab edge looks like when its two ends are joined across the wrong gap" - a PROXY for the fault the
    /// next gate measures directly (the share of the ring nobody drew, 10%, with its match-line exception). The
    /// proxy cannot tell an L-shaped tower floor from an invented one, and it refused 487,499 sq ft over 55 sets
    /// in run 43 - 188 rings, fifty of them between 50% and 55% - among them 30993's LEVEL 33-35 (7 storeys at
    /// zero, her 11,727 sq ft each) and 31202's ROOF, whose refusal is what parked step 132.
    /// WHAT THIS COVERS: an L drawn whole (50% of its box) is a floor; the same L closed across a quarter of its
    /// own perimeter is still refused, by the gate that measures the invention. WHAT IT DOES NOT: whether a thin
    /// ring that IS drawn whole should be a plate (a balcony band, a corridor strip - the corpus gate judges that
    /// against her models, and the render shows it).
    /// </summary>
    [Fact]
    public void AnLShapedFloorDrawnWholeIsAFloorAndOneInventedAcrossItsPerimeterIsNot()
    {
        var drawn = StructuralPlanClassifier.Classify(
            Ell(120), Options(), sheet: null, tags: new[] { Slab14(600, 600) });      // 120 in of 16,800 drawn: under 1%
        var plate = Assert.Single(drawn.Slabs);
        Assert.Equal(60_000, plate.Area / 144, 0);                                     // the L's own area, 50% of its box

        var invented = StructuralPlanClassifier.Classify(
            Ell(3600), Options(), sheet: null, tags: new[] { Slab14(600, 600) });      // 3,600 in against 13,200 drawn: 27%
        Assert.Empty(invented.Slabs);
    }
}
