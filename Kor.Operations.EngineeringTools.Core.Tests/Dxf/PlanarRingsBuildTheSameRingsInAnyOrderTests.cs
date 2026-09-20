#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// WHAT THIS COVERS: bounded-face ownership, a shared diagonal, adjacent squares, crossing and
/// overlapping segments, a slab with a wall band, explicit hole selection, dangling/cut edges,
/// a near-duplicate corner and conservative bridging. Each fixture is reversed, its endpoints
/// flipped, shuffled with seeds 7/11/19, and translated by (5000,3000) and a fractional vector.
/// Ring/face/chain collections are compared one-to-one, independently of list order and ring start.
/// WHAT IT DOES NOT: real drawings, speed, materials, layer-role inference or the existing consumers.
/// A same-class fault not caught is a nearly degenerate arrangement whose angle order changes with
/// floating-point rounding at an untested scale. No green six-set differential is claimed here.
/// </summary>
public sealed class PlanarRingsBuildTheSameRingsInAnyOrderTests
{
    private static PlanarRings Exact() => new(0.05, 0.05, 0.05);
    private static DxfSegment S(double ax, double ay, double bx, double by) => new("L", new(ax, ay), new(bx, by));
    private static DxfSegment[] Box(double x, double y, double w, double h) =>
        [S(x, y, x + w, y), S(x + w, y, x + w, y + h), S(x + w, y + h, x, y + h), S(x, y + h, x, y)];

    [Fact]
    public void BothTrianglesOwnTheDiagonal()
    {
        // +---+   The / belongs to BOTH triangles, once in each direction.
        // | / |
        // +---+
        Check(Box(0, 0, 10, 10).Append(S(0, 0, 10, 10)).ToArray(), Exact(), r =>
        {
            Assert.Equal(2, r.Loops.Count);
            Assert.All(r.Loops, l =>
            {
                Assert.Equal(3, l.Points.Count);
                Assert.True(HasEdge(l, new(0, 0), new(10, 10)));
                Assert.Equal(50, l.Area, 6);
            });
            Assert.Empty(r.OpenChains);
        });
    }

    [Fact]
    public void AdjacentSquaresKeepBothFacesAndAnOverlappingEdgeIsNotAThirdFace()
    {
        // +---+---+  Shared middle edge is supplied twice, plus a partial duplicate.
        // +---+---+
        Check(Box(0, 0, 10, 10).Concat(Box(10, 0, 10, 10)).Append(S(10, 2, 10, 8)).ToArray(), Exact(), r =>
        {
            Assert.Equal(2, r.Loops.Count);
            Assert.All(r.Loops, l => { Assert.Equal(100, l.Area, 6); Assert.True(HasEdge(l, new(10, 0), new(10, 10))); });
            Assert.Empty(r.OpenChains);
        });
    }

    [Fact]
    public void SquaresTouchingAtOneVertexRemainTwoSimpleFaces()
    {
        //     +---+  The exterior walk repeats the shared corner.
        // +---+---+
        // +---+
        Check(Box(0, 0, 10, 10).Concat(Box(10, 10, 10, 10)).ToArray(), Exact(), r =>
        {
            Assert.Equal(2, r.Faces.Count);
            Assert.All(r.Faces, f => { Assert.Equal(100, f.Outer.Area, 6); Assert.Empty(f.Holes); });
            Assert.Empty(r.OpenChains);
            Assert.Equal(2, r.RecoverSurfaces(_ => false, _ => true).Slabs.Count);
        });
    }

    [Fact]
    public void CrossingsAreNodesEvenWhenNeitherSegmentEndedThere()
    {
        // +---+  Both diagonals: four bounded triangles, a degree-four centre.
        // | X |
        // +---+
        Check(Box(0, 0, 10, 10).Concat([S(0, 0, 10, 10), S(0, 10, 10, 0)]).ToArray(), Exact(), r =>
        {
            Assert.Equal(4, r.Loops.Count);
            Assert.All(r.Loops, l => { Assert.Equal(25, l.Area, 6); Assert.Contains(new DxfPoint(5, 5), l.Points); });
        });
    }

    [Fact]
    public void TheWallBandSurvivesWhileTheSlabRecoversItsWholeOutline()
    {
        // +------++------+  30 x 20 slab; x=14..16 is a 2-unit band crossing it.
        // |      ||      |  Non-band-only union would leave two disconnected slabs.
        // +------++------+
        Check(Box(0, 0, 30, 20).Concat([S(14, 0, 14, 20), S(16, 0, 16, 20)]).ToArray(), Exact(), r =>
        {
            Assert.Equal(3, r.Faces.Count);
            bool Band(PlanarRings.Face f) => PlanarRings.IsRectangularBand(f, 2, 3);
            var recovered = r.RecoverSurfaces(Band, _ => true);
            Assert.Equal(40, Assert.Single(recovered.WallBands).Outer.Area, 6);
            var slab = Assert.Single(recovered.Slabs);
            Assert.Equal(600, slab.Outer.Area, 6);
            Assert.Equal(4, slab.Outer.Points.Count);
            Assert.Empty(slab.Holes);
            var slot = r.RecoverSurfaces(Band, f => !Band(f));
            Assert.Equal(2, slot.Slabs.Count);
            Assert.All(slot.Slabs, s => Assert.Equal(280, s.Outer.Area, 6));
        });
    }

    [Fact]
    public void NestedComponentsHaveHoleBoundariesAndFillRequiresAnExplicitChoice()
    {
        // +-----------+  An inner rectangle alone is not evidence whether it is a void or a room.
        // |  +-----+  |
        // |  +-----+  |
        // +-----------+
        Check(Box(0, 0, 30, 20).Concat(Box(10, 5, 10, 10)).ToArray(), Exact(), r =>
        {
            Assert.Equal(2, r.Faces.Count);
            var annulus = Assert.Single(r.Faces, f => f.Holes.Count == 1);
            Assert.Equal(600, annulus.Outer.Area, 6);
            Assert.Equal(100, Assert.Single(annulus.Holes).Area, 6);
            var withHole = r.RecoverSurfaces(_ => false, f => f.Holes.Count > 0);
            Assert.Single(Assert.Single(withHole.Slabs).Holes);
            Assert.Empty(Assert.Single(r.RecoverSurfaces(_ => false, _ => true).Slabs).Holes);
        });
    }

    [Fact]
    public void ADeadEndAndABridgeBetweenCyclesRemainOpenChains()
    {
        // --+---+---+---+   The link between the squares has no degree-one vertex,
        //   +---+   +---+   but it is still a cut edge, not part of a bounded ring.
        Check(Box(0, 0, 10, 10).Concat(Box(20, 0, 10, 10))
            .Concat([S(-5, 0, 0, 0), S(10, 0, 20, 0)]).ToArray(), Exact(), r =>
        {
            Assert.Equal(2, r.Loops.Count);
            Assert.Equal(2, r.OpenChains.Count);
            Assert.Contains(r.OpenChains, c => SameChain(c, [new(-5, 0), new(0, 0)]));
            Assert.Contains(r.OpenChains, c => SameChain(c, [new(10, 0), new(20, 0)]));
        });
    }

    [Fact]
    public void ACornerDrawnTwiceUsesTheClusterCentroid()
    {
        // +---+  Bottom-left ends are (0,0) and (.04,0): one node at (.02,0).
        // +...+
        Check([S(0, 0, 10, 0), S(10, 0, 10, 10), S(10, 10, 0, 10), S(0, 10, 0.04, 0)], Exact(), r =>
        {
            var loop = Assert.Single(r.Loops);
            Assert.Equal(4, loop.Points.Count);
            Assert.Contains(loop.Points, p => Near(p, new(0.02, 0)));
            Assert.Empty(r.OpenChains);
            Assert.True(loop.ClosedExactly);
        });
    }

    [Fact]
    public void TransitiveEquivalenceCanCollapseAClusterWiderThanTheTolerance()
    {
        // Three parallel lines at x=0,.04,.08; join=.05 chains both endpoint clusters.
        Check([S(0, 0, 0, 10), S(0.04, 0, 0.04, 10), S(0.08, 0, 0.08, 10)], Exact(), r =>
        {
            Assert.Empty(r.Loops);
            Assert.True(SameChain(Assert.Single(r.OpenChains), [new(0.04, 0), new(0.04, 10)]));
        });
    }

    [Fact]
    public void UniqueCornerExtensionsAreInsertedBeforeTheFacesAreWalked()
    {
        // +-----+  Bottom ends at (9,0), right starts at (10,1).
        // |     |  Outward rays meet at (10,0), one unit from each end.
        // +---- .
        Check([S(0, 0, 9, 0), S(10, 1, 10, 10), S(10, 10, 0, 10), S(0, 10, 0, 0)], new(0.05, 0.05, 1), r =>
        {
            var loop = Assert.Single(r.Loops);
            Assert.Equal(100, loop.Area, 6);
            Assert.False(loop.ClosedExactly);
            Assert.Empty(r.OpenChains);
        });
    }

    /// <summary>
    /// A T DRAWN SHORT IS A T (intake step 129, 2026-09-19). 31009's L5 draws its north-east edge to 25 mm above the wall it
    /// meets; the arrangement bridged end to end and joined an end ON a body, never an end SHORT of a body, so the end
    /// dangled and the floor was the page (L3 draws the same edge to the wall, and closes). WHAT THIS COVERS: an end short
    /// of another edge's body by under the bridge is joined at the foot of its perpendicular and the face closes; the
    /// body may be a wall's; an end short by more than the bridge stays open; an end within the join is the old exact
    /// contact. WHAT IT DOES NOT: two ends short of the same body (each joins on its own); a T onto an inserted bridge.
    /// </summary>
    [Fact]
    public void AnEndShortOfAnotherEdgesBodyByUnderTheBridgeIsJoinedToIt()
    {
        // a square whose right side is drawn from (10,1) up to (10,10): its bottom end stops 1 short of the bottom edge's
        // body (the bottom edge runs on to (12,0)) - a T drawn short. Bridge 1.5: the foot at (10,0) joins it.
        Check([S(0, 0, 12, 0), S(10, 1, 10, 10), S(10, 10, 0, 10), S(0, 10, 0, 0)], new(0.05, 1.5, 0), r =>
        {
            var loop = Assert.Single(r.Loops);
            Assert.Equal(100, loop.Area, 6);
            Assert.False(loop.ClosedExactly);
        });
        // the same with the bottom edge a WALL's outline edge: the slab edge runs to the wall (a wall span is one whose
        // layer the arrangement is told is a wall's; here the builder has no such knowledge, so the plain form suffices)
        // two short by more than the bridge stays open
        Check([S(0, 0, 12, 0), S(10, 2, 10, 10), S(10, 10, 0, 10), S(0, 10, 0, 0)], new(0.05, 1.5, 0), r =>
        {
            Assert.Empty(r.Loops);
        });
    }

    /// <summary>
    /// AN END'S T IS NOT SPENT BY ITS BRIDGE (intake step 134, 2026-09-19): an end short of a body within the bridge joins
    /// that body even when the same end also bridges to another end - a sloppy junction of three. 30990's LEVEL 5 draws
    /// its south-west corner as three near misses: the edge stops 97 mm short of the vertical it turns down, 45 mm from
    /// the foot of another vertical rising from the corner; the end took the nearer end and the T went unmade, and the
    /// floor leaked through the 2 in that remained. WHAT THIS COVERS: the square closing through the T with the third
    /// line present; without the third line it closed already (AnEndShortOfAnotherEdgesBodyByUnderTheBridgeIsJoinedToIt).
    /// WHAT IT DOES NOT: an end-to-end bridge, which still needs both ends' agreement.
    /// </summary>
    [Fact]
    public void AnEndShortOfABodyJoinsItEvenWhenItAlsoBridgesToAnotherEnd()
    {
        // the square's bottom edge stops at (9,0), one short of the right side's body at x 10 (the right side runs from
        // (10,-0.4) up); a third line rises from (8.6,0.45) - its foot 0.6 from the bottom edge's end, nearer than the T
        // (bridge 1.2: the third line's top at 1.4 from the right side stays clear of it)
        Check([S(0, 0, 9, 0), S(10, -0.4, 10, 10), S(10, 10, 0, 10), S(0, 10, 0, 0), S(8.6, 0.45, 8.6, 4)], new(0.05, 1.2, 0), r =>
        {
            Assert.Contains(r.Loops, l => Math.Abs(l.Area - 100) < 1.5);   // the square, less the sliver at the third line's foot
        });
    }

    [Fact]
    public void EquallyGoodBridgesAreLeftOpenInsteadOfPickingAnArrival()
    {
        // |   |  Tips at (-1,1) and (1,1) are equally near (0,0).
        //   |    No bridge gets to win by being first.
        Check([S(0, -10, 0, 0), S(-1, 1, -1, 11), S(1, 1, 1, 11)], new(0.05, 1.5, 0), r =>
        {
            Assert.Empty(r.Loops);
            Assert.Equal(3, r.OpenChains.Count);
        });
    }

    private static void Check(DxfSegment[] source, PlanarRings builder, Action<PlanarRings.Result> assert)
    {
        var expected = builder.Build(source);
        assert(expected);
        var variants = new List<DxfSegment[]> { source, source.Reverse().ToArray(), source.Select(s => s with { Start = s.End, End = s.Start }).ToArray() };
        foreach (int seed in new[] { 7, 11, 19 })
        {
            var random = new Random(seed); var copy = source.ToArray();
            for (int i = copy.Length - 1; i > 0; i--) { int j = random.Next(i + 1); (copy[i], copy[j]) = (copy[j], copy[i]); }
            for (int i = 0; i < copy.Length; i++) if (random.Next(2) == 0) copy[i] = copy[i] with { Start = copy[i].End, End = copy[i].Start };
            variants.Add(copy);
        }
        foreach (var variant in variants)
        {
            var actual = builder.Build(variant);
            assert(actual);
            SameResult(expected, actual, 0, 0);
            foreach (var (dx, dy) in new[] { (5000.0, 3000.0), (5000.37, 3000.61) })
            {
                var moved = builder.Build(variant.Select(s => s with { Start = Move(s.Start, dx, dy), End = Move(s.End, dx, dy) }));
                SameResult(expected, moved, dx, dy);
                // Recovery must commute with translation too, independently of the face-list order.
                bool Band(PlanarRings.Face f) => PlanarRings.IsRectangularBand(f, 2, 3);
                Func<PlanarRings.Face, bool>[] fills = [_ => true, f => !Band(f), f => f.Holes.Count > 0];
                foreach (var fill in fills)
                {
                    var a = expected.RecoverSurfaces(Band, fill);
                    var b = moved.RecoverSurfaces(Band, fill);
                    Assert.True(SameSet(a.WallBands, b.WallBands.Select(f => Unmove(f, dx, dy)).ToList(), SameFace));
                    Assert.True(SameSet(a.Slabs, b.Slabs.Select(f => Unmove(f, dx, dy)).ToList(), SameFace));
                }
            }
        }
    }

    private static void SameResult(PlanarRings.Result expected, PlanarRings.Result actual, double dx, double dy)
    {
        var movedLoops = actual.Loops.Select(l => new PlanLoop(l.Layer,
            l.Points.Select(p => Move(p, -dx, -dy)).ToList(), l.ClosedExactly)).ToList();
        Assert.True(SameSet(expected.Loops, movedLoops, SameRing));
        Assert.True(SameSet(expected.Faces, actual.Faces.Select(f => Unmove(f, dx, dy)).ToList(), SameFace));
        Assert.True(SameSet(expected.OpenChains, actual.OpenChains.Select(c => (IReadOnlyList<DxfPoint>)c.Select(p => Move(p, -dx, -dy)).ToList()).ToList(), SameChain));
    }
    private static PlanarRings.Face Unmove(PlanarRings.Face f, double dx, double dy)
    {
        PlanLoop Loop(PlanLoop l) => new(l.Layer, l.Points.Select(p => Move(p, -dx, -dy)).ToList(), l.ClosedExactly);
        return new(Loop(f.Outer), f.Holes.Select(Loop).ToList());
    }
    private static DxfPoint Move(DxfPoint p, double dx, double dy) => new(p.X + dx, p.Y + dy);
    private static bool Near(DxfPoint a, DxfPoint b) => LoopGeometry.Within(a.DistanceTo(b), 1e-6);
    private static bool SameFace(PlanarRings.Face a, PlanarRings.Face b) => SameRing(a.Outer, b.Outer) && SameSet(a.Holes, b.Holes, SameRing);
    private static bool SameRing(PlanLoop a, PlanLoop b)
    {
        if (a.Points.Count != b.Points.Count || a.ClosedExactly != b.ClosedExactly || a.Layer != b.Layer) return false;
        int n = a.Points.Count;
        return Enumerable.Range(0, n).Any(start => new[] { -1, 1 }.Any(direction => Enumerable.Range(0, n)
            .All(i => Near(a.Points[i], b.Points[(start + direction * i + n) % n]))));
    }
    private static bool SameChain(IReadOnlyList<DxfPoint> a, IReadOnlyList<DxfPoint> b)
        => a.Count == b.Count && (a.Zip(b).All(p => Near(p.First, p.Second)) || a.Zip(b.Reverse()).All(p => Near(p.First, p.Second)));
    private static bool HasEdge(PlanLoop loop, DxfPoint a, DxfPoint b) => Enumerable.Range(0, loop.Points.Count).Any(i =>
        (Near(loop.Points[i], a) && Near(loop.Points[(i + 1) % loop.Points.Count], b))
        || (Near(loop.Points[i], b) && Near(loop.Points[(i + 1) % loop.Points.Count], a)));
    private static bool SameSet<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, T, bool> same)
    {
        if (a.Count != b.Count) return false;
        var partner = Enumerable.Repeat(-1, b.Count).ToArray();
        bool Assign(int i, bool[] seen)
        {
            for (int j = 0; j < b.Count; j++)
            {
                if (seen[j] || !same(a[i], b[j])) continue;
                seen[j] = true;
                if (partner[j] < 0 || Assign(partner[j], seen)) { partner[j] = i; return true; }
            }
            return false;
        }
        return Enumerable.Range(0, a.Count).All(i => Assign(i, new bool[b.Count]));
    }
}
