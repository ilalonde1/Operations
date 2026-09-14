#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Two corpus runs laid side by side put every set in ONE class by the first thing that changed: a
/// model gained or lost, its storeys, which sheets stand on the grid, or - both the same - what the
/// composer made of the same sheets (intake step 59, 2026-09-13; `corpus-query diff`). Run 7 against
/// run 8 read 117 sets in the last class, which the six-set gate cannot see because all six sets
/// stand on grids.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: each class on a fixture pair, the class order (storeys before placement before
/// composition), the sums a class reports, and the yardstick verdict per set. WHAT IT DOES NOT: the
/// verb's printed table; a placement that changed to the same count of different sheets (the ledger
/// carries a count, so it reads as composition); why any set moved.
/// </remarks>
public sealed class WhatMovedBetweenTwoRunsIsClassedByTheFirstThingThatChangedTests
{
    private static readonly DateTime At = new(2026, 9, 13, 1, 0, 0, DateTimeKind.Utc);

    private static CorpusAnalyzer.SetRow Row(string job, bool model, int storeys, int placed, int columns, int walls, int plates, int? within100 = null, int? compared = null) =>
        new(Guid.Empty, At, At, job, "03 Residential", "structural", $@"\\fs\{job}.pdf", null, false, 1, 10, 5, 5, 0, 5, 0, storeys, model, model ? null : "no storeys",
            model ? placed : null, model ? storeys : null, model ? walls : null, model ? columns : null, null, model ? plates : null, 1.0, null,
            within100 is null ? null : "y.e2k", null, null, null, null, compared, null, within100, null, null, null);

    [Fact]
    public void EverySetFallsInOneClassAndTheFirstChangeNamesIt()
    {
        var before = new[]
        {
            Row("new", false, 0, 0, 0, 0, 0),
            Row("lost", true, 3, 2, 10, 5, 1),
            Row("storeys", true, 2, 2, 10, 5, 1),
            Row("placed", true, 4, 1, 100, 50, 2, within100: 40, compared: 100),
            Row("composed", true, 4, 4, 100, 50, 2, within100: 40, compared: 100),
            Row("same", true, 4, 4, 100, 50, 2),
            Row("never", false, 0, 0, 0, 0, 0),
            Row("gone", true, 1, 1, 1, 1, 1),
        };
        var after = new[]
        {
            Row("new", true, 3, 0, 30, 0, 1),
            Row("lost", false, 3, 0, 0, 0, 0),
            Row("storeys", true, 6, 1, 25, 5, 3),         // storeys AND placement moved: storeys names it
            Row("placed", true, 4, 3, 130, 50, 2, within100: 70, compared: 110),
            Row("composed", true, 4, 4, 80, 60, 3, within100: 30, compared: 90),
            Row("same", true, 4, 4, 100, 50, 2),
            Row("never", false, 0, 0, 0, 0, 0),
            Row("added", true, 1, 1, 1, 1, 1),
        };

        var r = CorpusDiff.Compare(before, after);

        Assert.Equal(["gone"], r.OnlyBefore);
        Assert.Equal(["added"], r.OnlyAfter);
        Assert.Equal(7, r.Movers.Count);
        Assert.Equal(CorpusDiff.Change.NewModel, r.Movers.Single(m => m.Job == "new").Change);
        Assert.Equal(CorpusDiff.Change.LostModel, r.Movers.Single(m => m.Job == "lost").Change);
        Assert.Equal(CorpusDiff.Change.Storeys, r.Movers.Single(m => m.Job == "storeys").Change);
        Assert.Equal(CorpusDiff.Change.Placement, r.Movers.Single(m => m.Job == "placed").Change);
        Assert.Equal(CorpusDiff.Change.Composition, r.Movers.Single(m => m.Job == "composed").Change);
        Assert.Equal(CorpusDiff.Change.Unchanged, r.Movers.Single(m => m.Job == "same").Change);
        Assert.Equal(CorpusDiff.Change.Unchanged, r.Movers.Single(m => m.Job == "never").Change);

        var composed = r.Movers.Single(m => m.Job == "composed");
        Assert.Equal(-20, composed.Columns);
        Assert.Equal(10, composed.Walls);
        Assert.Equal(1, composed.Plates);
        Assert.True(composed.HasYardstick);
        Assert.Equal(-10, composed.Within100);                                  // worse: 40 of 100 -> 30 of 90

        var placed = r.Movers.Single(m => m.Job == "placed");
        Assert.Equal(30, placed.Within100);                                     // better
        Assert.False(r.Movers.Single(m => m.Job == "same").HasYardstick);
        Assert.Equal(30, r.Of(CorpusDiff.Change.NewModel).Sum(m => m.Columns));
        Assert.Equal(-10, r.Of(CorpusDiff.Change.LostModel).Sum(m => m.Columns));
    }
}
