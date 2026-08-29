using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// What the engineer's slab COUNT is allowed to do to a sheet, and what it is not.
///
/// Andrea said three times that 31168's mezzanine carries three slabs. Banked as
/// slab-count.31168.LEVEL 1 MEZZ it stops being an argument and becomes behaviour — but the
/// behaviour has to be additive. A count says a floor is MISSING; it is not licence to re-read the
/// floors that are already there, and it is not licence to invent one on a sheet that draws none.
///
/// Each test here is a mistake that was actually made while this was being built.
/// </summary>
public class ACountSheGaveAddsAFloorTests
{
    /// <summary>A rectangle of slab edge, drawn as four separate segments that meet exactly.</summary>
    private static IEnumerable<DxfSegment> Rect(string layer, double x0, double y0, double x1, double y1)
    {
        var a = new DxfPoint(x0, y0);
        var b = new DxfPoint(x1, y0);
        var c = new DxfPoint(x1, y1);
        var d = new DxfPoint(x0, y1);
        yield return new DxfSegment(layer, a, b);
        yield return new DxfSegment(layer, b, c);
        yield return new DxfSegment(layer, c, d);
        yield return new DxfSegment(layer, d, a);
    }

    private static readonly PlanClassificationOptions Options = new();

    [Fact]
    [Trait("Speed", "Fast")]
    public void A_count_does_not_change_a_floor_the_drawing_already_closed()
    {
        // One clean 120x120 ft ring, and a count saying the storey carries three.
        var segments = Rect("JBP_C_SLABEDG", 0, 0, 1440, 1440).ToList();

        var without = StructuralPlanClassifier.Classify(segments, Options);
        var with = StructuralPlanClassifier.Classify(segments, Options with { ExpectedSlabCount = 3 });

        Assert.Single(without.Slabs);

        // The plate she has already seen must come back byte for byte. Widening the flood fill's
        // gate on her count let a raster reading swallow a ring that had closed as vectors: 31168's
        // mezzanine went from a 1,903 sq ft outline to a traced 2,330 the moment the count was
        // switched on, which is a rebuild of a storey she had accepted.
        Assert.Equal(without.Slabs[0].Area, with.Slabs[0].Area, 6);
        Assert.Equal(without.Slabs[0].Points.Count, with.Slabs[0].Points.Count);
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void A_count_adds_nothing_to_a_sheet_that_draws_no_slab_edge_at_all()
    {
        // Walls only. This is 31168's WEST (BLDG A & B) sheet, which serves the same storey name as
        // the YMCA's and closes no slab edge: its only plate is the perimeter-wall fallback. Told
        // the storey carries three, it went looking and produced a region in another building.
        var segments = Rect("JBP_V-WALL", 0, 0, 1440, 1440)
            .Concat(Rect("JBP_V-WALL", 8, 8, 1432, 1432))
            .ToList();

        var with = StructuralPlanClassifier.Classify(segments, Options with { ExpectedSlabCount = 3 });

        Assert.True(with.Slabs.Count <= 1,
            $"a sheet that draws no slab edge gained {with.Slabs.Count} floors from a count that is " +
            "about the storey, not about this drawing.");
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void A_slot_through_a_plate_is_not_a_step_in_its_edge()
    {
        // The shape the flood fill returns when the exterior reaches in through a break in the drawn
        // edge: a 40 ft square with an 18 in slit cut 20 ft into it. Right area, right position, and
        // a diaphragm cut nearly in two. It does not cross itself and its shortest edge is 18 in, so
        // nothing else catches it.
        var slotted = new List<DxfPoint>
        {
            new(0, 0), new(480, 0), new(480, 480), new(0, 480),
            new(0, 249), new(240, 249), new(240, 231), new(0, 231),
        };
        Assert.True(LoopGeometry.HasNarrowNeck(slotted, 36.0));
        Assert.False(LoopGeometry.SelfIntersects(slotted));

        // And the shape that must NOT be caught: a staircase of small steps. Its parallel edges sit
        // just as close together, and they run the SAME way with slab between them.
        var stepped = new List<DxfPoint>
        {
            new(0, 0), new(480, 0), new(480, 240),
            new(360, 240), new(360, 234), new(240, 234), new(240, 228), new(0, 228),
        };
        Assert.False(LoopGeometry.HasNarrowNeck(stepped, 36.0));
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void A_plate_that_crosses_itself_is_refused()
    {
        // KF7 reached the engineer's screen as a self-crossing ring and ETABS answered "Area Object
        // KF7 not correctly defined". The composer had always applied this test to openings and
        // never to floors; it lives in LoopGeometry now so both use the one implementation.
        var bowtie = new List<DxfPoint> { new(0, 0), new(480, 480), new(480, 0), new(0, 480) };
        Assert.True(LoopGeometry.SelfIntersects(bowtie));

        var square = new List<DxfPoint> { new(0, 0), new(480, 0), new(480, 480), new(0, 480) };
        Assert.False(LoopGeometry.SelfIntersects(square));
    }
}
