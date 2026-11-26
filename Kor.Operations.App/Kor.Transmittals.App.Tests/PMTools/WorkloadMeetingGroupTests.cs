#nullable enable
using System.Linq;
using Kor.Operations.Financials;
using Kor.Operations.PMTools;
using Xunit;

namespace Kor.Operations.App.Tests.PMTools;

/// <summary>
/// The PM sections of the Workload Meeting (spec docs/KOR-PMTools-Redesign-Spec-2026-11-26.md):
/// R9 totals over prioritised jobs only, R10's prioritised-unbilled order key, R14 grouping by
/// drafting manager.
///
/// Covers: which rows count as prioritised; the per-group prioritised fee / unbilled and their
/// in-place recompute after a priority changes; the group key for PM and drafting-manager
/// grouping, including a blank drafting manager.
/// Does NOT cover: PmToolsViewModel.BuildPmGroups end to end (it reads a WPF ICollectionView with
/// the window's filters), the button wiring, or the re-sort after load. A same-class fault it
/// would miss: BuildPmGroups ordering by total unbilled instead of prioritised unbilled, because
/// the ordering expression itself is not exercised here.
/// </summary>
public sealed class WorkloadMeetingGroupTests
{
    private static PmProjectRow Row(string wbs1, string pm, string dm, double fee, double billed, int priority)
    {
        var r = PmProjectRow.FromProject(new FinancialsProjectRow
        {
            Wbs1 = wbs1, Name = wbs1, Pm = pm, DraftingManager = dm, Fee = fee, FeeBilled = billed,
        }, 1.36);
        r.MeetingPriority = priority;
        return r;
    }

    [Fact]
    public void Group_TotalsPrioritisedJobsOnly_AndRecomputesInPlace()
    {
        var a = Row("A", "Conor", "Serban", fee: 100_000, billed: 40_000, priority: 1);
        var b = Row("B", "Conor", "Serban", fee: 50_000, billed: 10_000, priority: 0);
        var c = Row("C", "Conor", "", fee: 20_000, billed: 5_000, priority: 5);

        var g = new PmGroupViewModel("Conor", new[] { a, b, c });

        Assert.Equal(3, g.ProjectCount);
        Assert.Equal(170_000, g.TotalFee);
        Assert.Equal(2, g.PrioritisedCount);
        Assert.Equal(120_000, g.PrioritisedFee);
        Assert.Equal(75_000, g.PrioritisedUnbilled);   // (100k-40k) + (20k-5k)

        b.MeetingPriority = 2;
        g.RecomputePrioritised();
        Assert.Equal(3, g.PrioritisedCount);
        Assert.Equal(115_000, g.PrioritisedUnbilled);
        Assert.StartsWith("Prioritised: 3", g.PrioritisedSummary);
    }

    [Fact]
    public void Group_WithNoPriorities_SaysSo()
    {
        var g = new PmGroupViewModel("Jason", new[] { Row("X", "Jason", "Mark", 10_000, 0, 0) });
        Assert.Equal(0, g.PrioritisedCount);
        Assert.Equal("No prioritised jobs", g.PrioritisedSummary);
    }

    [Fact]
    public void GroupKey_ByPm_OrByDraftingManager_WithBlankLabelled()
    {
        var withDm = Row("A", " Conor Murtagh ", " Serban Acosti ", 1, 0, 1);
        var noDm = Row("B", "Conor Murtagh", "  ", 1, 0, 1);

        Assert.Equal("Conor Murtagh", PmToolsViewModel.GroupKey(withDm, byDraftingManager: false));
        Assert.Equal("Serban Acosti", PmToolsViewModel.GroupKey(withDm, byDraftingManager: true));
        Assert.Equal(PmToolsViewModel.NoDraftingManagerLabel, PmToolsViewModel.GroupKey(noDm, byDraftingManager: true));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void IsPrioritised_IsP1ToP5(int priority, bool expected)
        => Assert.Equal(expected, PmToolsViewModel.IsPrioritised(Row("A", "p", "d", 1, 0, priority)));
}
