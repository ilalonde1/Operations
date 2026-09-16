#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// WHAT CHANGED BETWEEN RUNS, PAGE BY PAGE, is a verb (`corpus-query pages`), not a scratch script
/// (2026-09-15: run 20 placed more sheets than run 21 on four sets, and only the per-page rows of
/// analysis.IntakeSheet could say that run 20 was the wrong one). <see cref="CorpusAnalyzer.PagesThatDiffer"/>
/// is the pure part: which pages the runs disagree on.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a page placed in one run and not the other; a page on different storeys; a page whose
/// title changed; a page absent from one run; pages the runs agree on left out; runs ordered by time not by
/// id. WHAT IT DOES NOT: the SQL (needs the database; the verb prints its own note when there is none);
/// geometry counts, which are the set-level diff's.
/// </remarks>
public sealed class ThePagesOfASetRunAgainstRunTests
{
    private static readonly Guid Earlier = Guid.Parse("ffffffff-0000-0000-0000-000000000001");   // a later id, an earlier run
    private static readonly Guid Later = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static CorpusAnalyzer.PageRun Row(Guid run, int hoursAgo, int page, bool? placed, string? storeys, string? title = "LEVEL 2") =>
        new(run, new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc).AddHours(-hoursAgo), page, $"S2.{page:00}", "plan", title, null, placed, storeys, 1, 20, 30, null);

    [Fact]
    public void OnlyThePagesTheRunsDisagreeOnAreListedInRunOrder()
    {
        var rows = new List<CorpusAnalyzer.PageRun>
        {
            Row(Earlier, 5, 1, true, "L2"),          Row(Later, 2, 1, true, "L2"),            // agreed
            Row(Earlier, 5, 2, true, "L3"),          Row(Later, 2, 2, false, null),           // placed, then not
            Row(Earlier, 5, 3, true, "L4"),          Row(Later, 2, 3, true, "L4,L5"),         // different storeys
            Row(Earlier, 5, 4, true, "L6", "LEVEL 6 SLAB"), Row(Later, 2, 4, true, "L6", "LEVEL 6 SLAB REINFORCING"),   // the reading moved
            Row(Earlier, 5, 5, false, null),                                                  // absent from the later run
        };

        var differ = CorpusAnalyzer.PagesThatDiffer(rows);

        Assert.Equal(new[] { 2, 3, 4, 5 }, differ.Select(d => d.Page));
        var p2 = differ.Single(d => d.Page == 2).ByRun;
        Assert.Equal(2, p2.Count);
        Assert.Equal(Earlier, p2[0]!.RunId);      // the run with the later id but the earlier time comes first
        Assert.True(p2[0]!.Placed);
        Assert.False(p2[1]!.Placed);
        Assert.Null(differ.Single(d => d.Page == 5).ByRun[1]);
    }

    [Fact]
    public void NoRowsAndOneRunDifferOnNothing()
    {
        Assert.Empty(CorpusAnalyzer.PagesThatDiffer([]));
        Assert.Empty(CorpusAnalyzer.PagesThatDiffer([Row(Earlier, 1, 1, true, "L2"), Row(Earlier, 1, 2, false, null)]));
    }
}
