#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// A HOLE TOUCHING ITS FACE'S BOUNDARY AT A VERTEX IS THAT FACE'S HOLE (2026-09-17). A box drawn inside a floor with one
/// corner ON the floor's edge (a shaft against the slab edge, a T carried short onto an edge that closes a loop there)
/// makes one face walk that passes the corner twice; PlanarRings splits it into the floor's ring and the box's ring,
/// and the box's ring - in the floor's own component, which the containment rule reserved for the outside's rings -
/// belonged to nobody. The floor's face then had no hole, and recovering the floor and the box together met two
/// half-edges with one successor at the corner: "Face successor is not a permutation", which refused 31065's P1
/// north (its floor, 11,442 sq ft, 37 columns) under step 112 and its design-load plan without it.
/// WHAT THIS COVERS: a 10 x 10 m floor with a 1 x 1 m box inside it, one corner of the box on the floor's edge (not
/// at the floor's corner, so no edge overlaps): the floor's face carries the box as a hole; recovering both cells is
/// one slab with no hole; recovering the floor alone is one slab with one hole; and the same three whatever order
/// the lines come in. WHAT IT DOES NOT: the real sheets (the six-set gate); a box sharing an EDGE with the floor
/// (two ordinary faces); a box touching at a corner from OUTSIDE (two faces, the outside's walk split there).
/// </summary>
public sealed class AHoleTouchingItsFaceAtAVertexIsItsHoleTests
{
    private static DxfSegment S(double ax, double ay, double bx, double by) => new("L", new(ax, ay), new(bx, by));

    private static DxfSegment[] FloorWithABoxOnItsEdge() =>
    [
        S(0, 0, 10000, 0), S(10000, 0, 10000, 10000), S(10000, 10000, 0, 10000), S(0, 10000, 0, 0),
        // the box's corner (4000, 0) sits on the floor's south edge, its sides at 45 degrees, so nothing overlaps the edge
        S(4000, 0, 4707, 707), S(4707, 707, 4000, 1414), S(4000, 1414, 3293, 707), S(3293, 707, 4000, 0),
    ];

    [Fact]
    public void TheFloorCarriesTheBoxAsAHole_AndBothCellsRecoverAsOneSlab()
    {
        var source = FloorWithABoxOnItsEdge();
        foreach (var variant in new[] { source, source.Reverse().ToArray(), source.Select(s => s with { Start = s.End, End = s.Start }).ToArray() })
        {
            var result = new PlanarRings(3, 152.4, 3).Build(variant);
            Assert.Equal(2, result.Faces.Count);
            var floor = result.Faces.Single(f => Math.Abs(f.Outer.Area) > 50 * 1e6);
            var box = result.Faces.Single(f => Math.Abs(f.Outer.Area) < 50 * 1e6);
            var hole = Assert.Single(floor.Holes);
            Assert.InRange(Math.Abs(hole.Area), Math.Abs(box.Outer.Area) * 0.999, Math.Abs(box.Outer.Area) * 1.001);
            Assert.Empty(box.Holes);

            var both = result.RecoverSurfaces(_ => false, (_, _) => true);
            var slab = Assert.Single(both.Slabs);
            Assert.Empty(slab.Holes);
            Assert.InRange(Math.Abs(slab.Outer.Area), 100 * 1e6 * 0.999, 100 * 1e6 * 1.001);

            var floorOnly = result.RecoverSurfaces(_ => false, (_, f) => Math.Abs(f.Outer.Area) > 50 * 1e6);
            var plate = Assert.Single(floorOnly.Slabs);
            Assert.Single(plate.Holes);
        }
    }
}
