using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// 31009's L5 corner as drawn (intake step 129, 2026-09-19): the east edge in two lines 325 mm apart - the south one
/// (x 55,494) ending at y 15,626, the north one (x 55,819) starting at y 14,147 - a wall (55,628-55,933, y 10,794-14,122)
/// between them whose cap the north edge stops 25 mm short of, and the wall's faces also drawn as lines. WHAT THIS COVERS:
/// the T drawn short joins the north edge to the wall's cap (the open chain's end moves from (55,819, 14,147) to the cap);
/// and the truth it leaves: the south edge runs 134 mm west of the wall's corner, and a WALL'S CORNER IS NO END - no T
/// is proposed from it to the edge's body - so the floor stays open there. WHAT IT DOES NOT: the rule for that corner
/// (a wall standing beside a slab edge closer than the bridge), which is the morning's; the real page (the trace).
/// </summary>
public sealed class ATDrawnShortOntoAWallProbe
{
    private static DxfSegment S(string layer, double ax, double ay, double bx, double by) => new(layer, new(ax, ay), new(bx, by));

    [Fact]
    public void TheNorthEdgeShortOfTheWallsCapJoinsItAndTheSouthEdgeBesideTheWallsCornerStaysOpen()
    {
        var segs = new List<DxfSegment>
        {
            S("SLABEDGE", 35000, 8000, 35000, 20000), S("SLABEDGE", 35000, 20000, 55819, 20000), S("SLABEDGE", 35000, 8000, 55494, 8000),
            S("SLABEDGE", 55494, 15626, 55494, 8000), S("SLABEDGE", 55819, 14147, 55819, 20000),
            S("WALL", 55628, 10794, 55933, 10794), S("WALL", 55933, 10794, 55933, 14122), S("WALL", 55933, 14122, 55628, 14122), S("WALL", 55628, 14122, 55628, 10794),
            S("SLABEDGE", 55628, 14122, 55628, 10794), S("SLABEDGE", 55933, 10794, 55933, 14122), S("SLABEDGE", 55933, 14122, 55628, 14122),
        };
        var r = new PlanarRings(1.0, 152.4, 1219.2).Build(segs);
        var chain = Assert.Single(r.OpenChains);
        var ends = new[] { chain[0], chain[^1] };
        Assert.Contains(ends, p => Math.Abs(p.X - 55819) < 1 && Math.Abs(p.Y - 14122) < 1);   // the north edge, joined to the cap
        Assert.Contains(ends, p => Math.Abs(p.X - 55494) < 1 && Math.Abs(p.Y - 15626) < 1);   // the south edge's top, still in air
        Assert.DoesNotContain(r.Loops, l => Math.Abs(l.Area) / 92903.04 > 2000);              // and no floor yet
    }
}
