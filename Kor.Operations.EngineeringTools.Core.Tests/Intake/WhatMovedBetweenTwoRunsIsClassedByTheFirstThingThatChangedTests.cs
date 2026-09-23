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
/// WHAT THIS COVERS: each class on a fixture pair, the class order (storeys before views before
/// placement before composition), the sums a class reports, the verdict by share (worse by count and
/// better by share reads better), and a complete recall miss judged. WHAT IT DOES NOT: the verb's
/// printed table; a placement or composition that changed to the same counts (SameCounts: the ledger
/// carries counts); why any set moved.
/// </remarks>
public sealed class WhatMovedBetweenTwoRunsIsClassedByTheFirstThingThatChangedTests
{
    private static readonly DateTime At = new(2026, 9, 13, 1, 0, 0, DateTimeKind.Utc);

    private static CorpusAnalyzer.SetRow Row(string job, bool model, int storeys, int placed, int columns, int walls, int plates, int? within100 = null, int? compared = null) =>
        new(Guid.Empty, At, At, job, "03 Residential", "structural", $@"\\fs\{job}.pdf", null, false, 1, 10, 5, 5, 0, 5, 0, storeys, model, model ? null : "no storeys",
            model ? placed : null, model ? storeys : null, model ? walls : null, model ? columns : null, null, model ? plates : null, 1.0, null,
            within100 is null ? null : "y.e2k", null, null, null, null, compared, null, within100, null, null, null);

    /// <summary>
    /// A SET THAT READ A DIFFERENT STICK FILE IS NOT A MOVER (2026-09-23), and it is asked first so nothing
    /// downstream reads as a regression.
    ///
    /// The run 43 -> run 44 diff named 31039-01 the corpus's biggest loser — 35 plates down to 7, its placed
    /// sheets 39 of 40 down to 20 of 40, 1,271 columns down to 797 — and it was on the list as a regression to
    /// chase. It had been re-issued that week: "31039-01 2026-09-14 skyliving Stickfile.pdf", 77 pages and 32 MB,
    /// became the 2026-09-22 issue at 68 pages and 23 MB. ELEVEN of the 292 sets in both runs read a different
    /// file, and one of them (31130-01) is a six-set gate set.
    ///
    /// WHAT THIS COVERS: a different path, and the same path at a different size. WHAT IT DOES NOT: a file
    /// rewritten under the same name AND the same byte count, which looks identical to the ledger.
    /// </summary>
    [Fact]
    public void ASetThatReadADifferentStickFileIsClassedAsReIssuedBeforeAnythingElse()
    {
        var was = Row("reissued", true, 36, 39, 1271, 733, 35);
        var now = Row("reissued", true, 35, 20, 797, 415, 7) with { Pdf = @"\\fs\reissued 2026-09-22.pdf" };
        Assert.Equal(CorpusDiff.Change.ReIssued, CorpusDiff.Classify(was, now));

        // the same name, a different size: a re-issue that kept its file name
        var resized = Row("reissued", true, 35, 20, 797, 415, 7) with { Bytes = was.Bytes + 1 };
        Assert.Equal(CorpusDiff.Change.ReIssued, CorpusDiff.Classify(was, resized));

        // and the same file is judged on its counts as before - the class must not swallow real movement
        Assert.Equal(CorpusDiff.Change.Storeys, CorpusDiff.Classify(was, Row("reissued", true, 35, 39, 1271, 733, 35)));

        // ⚠ the one it cannot see, said out loud rather than left to be discovered
        var rewrittenSameSize = Row("reissued", true, 35, 20, 797, 415, 7);
        Assert.NotEqual(CorpusDiff.Change.ReIssued, CorpusDiff.Classify(was, rewrittenSameSize));
    }

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
            Row("views", true, 4, 4, 100, 50, 2) with { SheetsWritten = 6 },
            Row("share", true, 4, 4, 100, 50, 2, within100: 8, compared: 64),                        // 8 of 64 -> 5 of 10: worse by count, better by share
            Row("miss", true, 4, 4, 100, 50, 2, within100: 1, compared: 1) with { TheirsCompared = 1, TheirsWithin100 = 1 },
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
            Row("views", true, 4, 4, 100, 50, 2) with { SheetsWritten = 7 },
            Row("share", true, 4, 4, 100, 50, 2, within100: 5, compared: 10),
            Row("miss", true, 4, 4, 100, 50, 2, within100: 0, compared: 0) with { TheirsCompared = 1, TheirsWithin100 = 0 },   // none of ours inside her footprint now: a complete recall miss
        };

        var r = CorpusDiff.Compare(before, after);

        Assert.Equal(["gone"], r.OnlyBefore);
        Assert.Equal(["added"], r.OnlyAfter);
        Assert.Equal(10, r.Movers.Count);
        Assert.Equal(CorpusDiff.Change.Views, r.Movers.Single(m => m.Job == "views").Change);
        var share = r.Movers.Single(m => m.Job == "share");
        Assert.Equal(-3, share.Within100);                                       // worse by count
        Assert.Equal(1, share.Verdict);                                          // better by share: 12.5% -> 50%
        var miss = r.Movers.Single(m => m.Job == "miss");
        Assert.True(miss.HasYardstick);                                          // a complete recall miss is judged, not dropped
        Assert.Equal(-1, miss.Verdict);
        Assert.Equal(CorpusDiff.Change.NewModel, r.Movers.Single(m => m.Job == "new").Change);
        Assert.Equal(CorpusDiff.Change.LostModel, r.Movers.Single(m => m.Job == "lost").Change);
        Assert.Equal(CorpusDiff.Change.Storeys, r.Movers.Single(m => m.Job == "storeys").Change);
        Assert.Equal(CorpusDiff.Change.Placement, r.Movers.Single(m => m.Job == "placed").Change);
        Assert.Equal(CorpusDiff.Change.Composition, r.Movers.Single(m => m.Job == "composed").Change);
        Assert.Equal(CorpusDiff.Change.SameCounts, r.Movers.Single(m => m.Job == "same").Change);
        Assert.Equal(CorpusDiff.Change.SameCounts, r.Movers.Single(m => m.Job == "never").Change);

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
