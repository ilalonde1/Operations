#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The ledger's population is every path on the page, not every path the classifier happened to
/// receive. Found 2026-09-08 on the first run of the step 1 ledger: the intake parsed with the
/// classifier's thinning, the thinning dropped 85–91% of the paths on 31130's hatched parkade
/// plans before any rule saw them, and the ledger reported every remaining path accounted for.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the remap from thinned indices to population indices by subpath ordinal, the
/// annotation paths appended after content paths, and that every population index comes back
/// exactly once with the removed ones as CollapsedByThinning.
/// WHAT IT DOES NOT: that the reader's ordinals are right on a real page — no test checks that;
/// FiveStickFilesTests counts fates on the banked pages, which proves cardinality, not that each
/// fate sits on the shape it was decided for. Since audit F7 (2026-09-08) the remap fails loudly
/// on a path decided twice or a retained path decided never (TheAuditsCounterexamplesTests), so
/// "collapsed by thinning" can no longer paper over a missing decision.
/// </remarks>
public sealed class ThePopulationIsTheUnthinnedReadTests
{
    [Fact]
    public void EveryPopulationIndexComesBackOnceAndTheRemovedOnesAreCollapsed()
    {
        // full read kept ordinals 0,1,2,3,5,6 (ordinal 4 was a lone moveto in both reads),
        // then one annotation path → 7 population paths
        int[] fullKept = { 0, 1, 2, 3, 5, 6 };
        // thinned read kept ordinals 0,2,6 (1,3,5 thinned to a point), then the same annotation → 4 thinned paths
        int[] thinnedKept = { 0, 2, 6 };
        var thinnedFates = new[]
        {
            new PathFate(0, Disposition.Read, PathReason.BecameSlab, 0),
            new PathFate(1, Disposition.Unaccounted, PathReason.EmittedAsLine, 0),
            new PathFate(2, Disposition.Discarded, PathReason.FurnitureRegion, null),
            new PathFate(3, Disposition.Read, PathReason.BecameColumnByShape, 0),   // the annotation path
        };

        var fates = DrawingIntake.RemapToPopulation(thinnedFates, thinnedKept, fullKept, thinnedPathCount: 4, fullPathCount: 7);

        Assert.Equal(7, fates.Count);
        Assert.Equal(Enumerable.Range(0, 7), fates.Select(f => f.PathIndex).OrderBy(i => i));
        Assert.Equal(PathReason.BecameSlab, fates[0].Reason);
        Assert.Equal(PathReason.CollapsedByThinning, fates[1].Reason);
        Assert.Equal(PathReason.EmittedAsLine, fates[2].Reason);
        Assert.Equal(PathReason.CollapsedByThinning, fates[3].Reason);
        Assert.Equal(PathReason.CollapsedByThinning, fates[4].Reason);
        Assert.Equal(PathReason.FurnitureRegion, fates[5].Reason);
        Assert.Equal(PathReason.BecameColumnByShape, fates[6].Reason);
        Assert.All(fates.Where(f => f.Reason == PathReason.CollapsedByThinning), f => Assert.Equal(Disposition.Discarded, f.Disposition));
    }

    [Fact]
    public void NoThinningMeansNoCollapsedPaths()
    {
        int[] kept = { 0, 1, 2 };
        var fates = new[] { new PathFate(0, Disposition.Read, PathReason.BecameSlab, 0), new PathFate(1, Disposition.Discarded, PathReason.TooShort, null), new PathFate(2, Disposition.Unaccounted, PathReason.EmittedAsLine, 0) };
        var result = DrawingIntake.RemapToPopulation(fates, kept, kept, 3, 3);
        Assert.Equal(3, result.Count);
        Assert.DoesNotContain(result, f => f.Reason == PathReason.CollapsedByThinning);
        Assert.Equal(new[] { 0, 1, 2 }, result.Select(f => f.PathIndex));
    }
}
