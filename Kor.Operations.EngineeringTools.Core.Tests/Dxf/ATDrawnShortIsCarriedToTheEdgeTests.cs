#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// WHAT IT COVERS: a T drawn four inches short, a foot two millimetres from a target
/// vertex, and two ends proposing carries onto the same span.
/// WHAT IT DOES NOT: the real sheet; a carry the agreement refuses is not recovered.
/// These shapes already recovered before the fix; the near-vertex assertion gates
/// rejection of a sub-tolerance split, not the real sheet's permutation exception.
/// </summary>
public sealed class PlanarRingsATDrawnShortIsCarriedToTheEdgeTests
{
    private static DxfSegment S(double ax, double ay, double bx, double by) => new("L", new(ax, ay), new(bx, by));

    [Fact]
    public void TDrawnFourInchesShortRecoversOneSlab()
    {
        Check([S(0, 1000, 0, 0), S(0, 0, 1000, 0), S(1000, 0, 1000, 500), S(1000, 500, 101.6, 500)], 1);
    }

    [Fact]
    public void FootTwoMillimetresFromTargetVertexRecoversOneSlab()
    {
        // The 100 mm target span passes the fractional t bounds, but its 2 mm half
        // would collapse at join=3 mm; the end-to-end bridge can still close the slab.
        Check([S(0, 502, 0, 402), S(0, 402, 0, 0), S(0, 0, 1000, 0),
            S(1000, 0, 1000, 500), S(1000, 500, 101.6, 500)], 0);
    }

    [Fact]
    public void TwoEndsProposingCarriesOntoOneSpanRecoverOneSlab()
    {
        Check([S(0, 1000, 0, 0), S(0, 0, 1000, 0), S(1000, 0, 1000, 500),
            S(1000, 500, 101.6, 500), S(1000, 250, 127, 250)], 1);
    }

    private static void Check(DxfSegment[] source, int carries)
    {
        foreach (var variant in new[] { source, source.Reverse().ToArray(),
            source.Select(s => s with { Start = s.End, End = s.Start }).ToArray() })
        {
            var result = new PlanarRings(3, 152.4, 3).Build(variant);
            Assert.Single(result.Faces);
            Assert.Equal(carries, result.Carries.Count);
            var slab = Assert.Single(result.RecoverSurfaces(_ => false, (_, _) => true).Slabs);
            Assert.Empty(slab.Holes);
        }
    }
}
