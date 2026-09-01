using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The same ground described twice is one floor, not a floor with a hole in it.
///
/// This is the fault the engineer rejected the model over on 25 August: "on several levels
/// (9, 3, mezz, 1) he inverted slab and opening", with a region marked SHOULD BE SLAB. LEVEL 1
/// went out as a 78,859 sq ft plate with 74,832 sq ft cut out of it — the real slab, subtracted
/// from a flood fill of the same linework — and C-LEVEL 3, C-LEVEL 9 and the mezzanine the same
/// way.
///
/// Her ruling is a-plate-recovered-twice-is-not-a-hole: "keep the larger and drop the smaller.
/// The inner one is NOT an opening." A ring the DRAWING puts inside a floor is still an opening;
/// this is only about the tool reading one floor twice.
/// </summary>
public class PlateReadTwiceTests
{
    private readonly ITestOutputHelper _out;

    public PlateReadTwiceTests(ITestOutputHelper output) => _out = output;

    private const string SlabLayer = "JBP_C_SLABEDG";

    private static IEnumerable<DxfSegment> Ring(string layer, double x0, double y0, double x1, double y1)
    {
        yield return new DxfSegment(layer, new DxfPoint(x0, y0), new DxfPoint(x1, y0));
        yield return new DxfSegment(layer, new DxfPoint(x1, y0), new DxfPoint(x1, y1));
        yield return new DxfSegment(layer, new DxfPoint(x1, y1), new DxfPoint(x0, y1));
        yield return new DxfSegment(layer, new DxfPoint(x0, y1), new DxfPoint(x0, y0));
    }

    /// <summary>
    /// The shape 31168 actually produced. The outer slab edge is interrupted often enough that no
    /// chain of it closes, so only the raster fill recovers it. Inside it, a second run of
    /// slab-edge linework closes into a ring — and that ring is a step or a depression drawn on
    /// the same floor, not a hole through it.
    ///
    /// Both guards in the reader have to be satisfied for this path to run at all, so the fixture
    /// carries them: the fill refuses fewer than twelve segments, and the block only runs where a
    /// floor was closed by joining a chain's ends.
    /// </summary>
    [Fact]
    public void AFloorFoundTwiceIsOnePlateAndNoOpening()
    {
        var segments = new List<DxfSegment>();

        // Outer edge, drawn the way a real one is — in pieces, with interruptions the vectors
        // cannot bridge and the fill can.
        void Run(double x0, double y0, double x1, double y1, int pieces, double gap)
        {
            for (int i = 0; i < pieces; i++)
            {
                double a = i / (double)pieces, b = (i + 1) / (double)pieces;
                var start = new DxfPoint(x0 + (x1 - x0) * a, y0 + (y1 - y0) * a);
                var end = new DxfPoint(x0 + (x1 - x0) * b - (x1 - x0 != 0 ? gap : 0),
                                       y0 + (y1 - y0) * b - (y1 - y0 != 0 ? gap : 0));
                segments.Add(new DxfSegment(SlabLayer, start, end));
            }
        }

        Run(0, 0, 2400, 0, 4, 24);
        Run(2400, 0, 2400, 1800, 4, 24);
        Run(2400, 1800, 0, 1800, 4, -24);
        Run(0, 1800, 0, 0, 4, -24);

        // The inner reading: a step drawn on the same floor, its fourth side INTERRUPTED the way
        // crossing linework interrupts one, with the thickness call-out that says it is slab.
        //
        // It was three sides with the fourth absent, which SlabChainJoinFraction now refuses --
        // a join of 1,500 against 5,500 drawn is 27 per cent, the band where the tool would be
        // inventing a slab edge rather than recovering one. Refused, the inner ring never reaches
        // the rule this test is about. A 120 in interruption is 2 per cent and is what a real one
        // looks like.
        segments.Add(new DxfSegment(SlabLayer, new DxfPoint(200, 150), new DxfPoint(2200, 150)));
        segments.Add(new DxfSegment(SlabLayer, new DxfPoint(2200, 150), new DxfPoint(2200, 1650)));
        segments.Add(new DxfSegment(SlabLayer, new DxfPoint(2200, 1650), new DxfPoint(200, 1650)));
        segments.Add(new DxfSegment(SlabLayer, new DxfPoint(200, 1650), new DxfPoint(200, 960)));
        segments.Add(new DxfSegment(SlabLayer, new DxfPoint(200, 840), new DxfPoint(200, 150)));

        var options = new PlanClassificationOptions
        {
            SlabLayerPatterns = new[] { SlabLayer },
            WallLayerPatterns = new[] { "JBP_V-WALL" },
            ColumnLayerPatterns = new[] { "JBP_V_COL" },
        };

        var set = StructuralPlanClassifier.Classify(
            segments, options, sheet: null,
            tags: new[] { new DxfPositionedTag("14\" SLAB", new DxfPoint(1200, 900), "A-FLOR-IDEN", "14\" SLAB") });

        foreach (string f in set.Flags) _out.WriteLine(f);
        foreach (var s in set.Slabs) _out.WriteLine($"slab {s.Area / 144:N0} sq ft");
        _out.WriteLine($"slabs {set.Slabs.Count}, openings {set.Openings.Count}");

        // The inner ring must not come back as a hole. That is the whole ruling.
        Assert.Empty(set.Openings);

        // One plate, and it is the OUTER one — the ground both readings describe. Without this the
        // test passes on a run that simply lost the recovered floor: the inner ring alone is
        // 2,000 x 1,500 in = 20,833 sq ft, the outer 2,400 x 1,800 in = 30,000.
        var kept = Assert.Single(set.Slabs);
        Assert.True(kept.Area / 144 > 20_000, $"kept the smaller reading: {kept.Area / 144:N0} sq ft");

        // And it says so, because a plate silently dropped is how this went wrong the first time.
        Assert.Contains(set.Flags, f =>
            f.Contains("same floor read twice", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The other half of the rule, so the fix cannot be "never cut an opening". A ring the drawing
    /// puts inside a floor that closed on its own vectors is still a hole.
    /// </summary>
    [Fact]
    public void ARingDrawnInsideAClosedFloorIsStillAnOpening()
    {
        var segments = new List<DxfSegment>();
        segments.AddRange(Ring(SlabLayer, 0, 0, 1200, 900));
        segments.AddRange(Ring(SlabLayer, 300, 250, 800, 650));

        var options = new PlanClassificationOptions
        {
            SlabLayerPatterns = new[] { SlabLayer },
            WallLayerPatterns = new[] { "JBP_V-WALL" },
            ColumnLayerPatterns = new[] { "JBP_V_COL" },
        };

        var set = StructuralPlanClassifier.Classify(segments, options);

        foreach (string f in set.Flags) _out.WriteLine(f);
        _out.WriteLine($"slabs {set.Slabs.Count}, openings {set.Openings.Count}");

        Assert.Single(set.Slabs);
        Assert.Single(set.Openings);
    }
}
