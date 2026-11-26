#nullable enable
using System;
using System.Linq;
using Kor.Operations.App.PMTools;
using Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.App.Tests.PMTools;

/// <summary>
/// The top list of the Workload Meeting (spec docs/KOR-PMTools-Redesign-Spec-2026-11-26.md R1-R5,
/// Jim 9 Nov): prioritised projects ordered by their next Deltek deadline inside a look-ahead
/// window, and prioritised projects with no future deadline listed apart.
///
/// Covers: the pick of a project's next deadline from Projects_Deadlines rows, and the split into
/// due / no-deadline / later-than-window, including the window edge and P0 rows.
/// Does NOT cover: the ODBC read itself (dates and the CustDateCompleted filter live in SQL), the
/// XAML, or that the window passes today's date. A same-class fault it would miss: a SQL filter
/// that drops today's deadlines (>= vs >) passes here because these tests build the rows by hand.
/// </summary>
public sealed class WorkloadMeetingDueListTests
{
    private static readonly DateTime Today = new(2026, 11, 26);

    private static WorkloadMeetingProjectRow Row(string wbs1, int priority, DateTime? due, string name = "")
        => new()
        {
            Wbs1 = wbs1,
            Priority = priority,
            ProjectName = name.Length == 0 ? wbs1 : name,
            NextDeadline = due is null ? null : new ProjectDeadline(wbs1, due.Value, "Progress", ""),
        };

    [Fact]
    public void PickNextDeadlines_TakesTheEarliestRowPerProject()
    {
        var picked = FinancialsService.PickNextDeadlines(new[]
        {
            new ProjectDeadline("31057-01", new DateTime(2026, 12, 9), "BP", ""),
            new ProjectDeadline("31057-01", new DateTime(2026, 11, 27), "Progress", "100% BP draft"),
            new ProjectDeadline("31120-01", new DateTime(2026, 11, 27), "IFC", "Full IFC"),
            new ProjectDeadline("  ", new DateTime(2026, 11, 27), "IFC", ""),
        });

        Assert.Equal(2, picked.Count);
        Assert.Equal(new DateTime(2026, 11, 27), picked["31057-01"].Date);
        Assert.Equal("Progress", picked["31057-01"].Issue);
        Assert.True(picked.ContainsKey("31120-01".ToUpperInvariant()));
    }

    [Fact]
    public void Split_OrdersDueByDate_ListsNoDeadlineApart_AndCountsLater()
    {
        var rows = new[]
        {
            Row("A", 3, Today.AddDays(10)),
            Row("B", 1, Today),               // due today counts
            Row("C", 2, null),                // prioritised, no deadline
            Row("D", 1, Today.AddDays(14)),   // the window's last day counts
            Row("E", 1, Today.AddDays(15)),   // later than 2 weeks
            Row("F", 0, Today.AddDays(1)),    // not prioritised: never listed
        };

        var (due, none, later) = WorkloadMeetingPanelViewModel.SplitByDeadline(rows, Today, weeks: 2);

        Assert.Equal(new[] { "B", "A", "D" }, due.Select(r => r.Wbs1));
        Assert.Equal(new[] { "C" }, none.Select(r => r.Wbs1));
        Assert.Equal(1, later);
    }

    [Fact]
    public void Split_WiderWindowPullsLaterProjectsIn()
    {
        var rows = new[] { Row("D", 1, Today.AddDays(14)), Row("E", 1, Today.AddDays(15)) };

        var (due, _, later) = WorkloadMeetingPanelViewModel.SplitByDeadline(rows, Today, weeks: 3);

        Assert.Equal(new[] { "D", "E" }, due.Select(r => r.Wbs1));
        Assert.Equal(0, later);
    }

    [Fact]
    public void Split_SameDate_OrdersByPriorityThenName()
    {
        var day = Today.AddDays(3);
        var rows = new[] { Row("X", 2, day, "Beta"), Row("Y", 1, day, "Zeta"), Row("Z", 2, day, "Alpha") };

        var (due, _, _) = WorkloadMeetingPanelViewModel.SplitByDeadline(rows, Today, weeks: 2);

        Assert.Equal(new[] { "Y", "Z", "X" }, due.Select(r => r.Wbs1));
    }

    [Theory]
    [InlineData("RV", "Revit")]
    [InlineData(" rv ", "Revit")]
    [InlineData("CAD", "CAD")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void DraftingSoftware_ReadsDeltekCodes(string? deltek, string shown)
        => Assert.Equal(shown, Kor.Operations.PMTools.PmProjectRow.DraftingSoftwareLabel(deltek));

    [Fact]
    public void NextDeadlineWhat_JoinsIssueAndRemarks()
    {
        var both = new WorkloadMeetingProjectRow { NextDeadline = new ProjectDeadline("A", Today, "Progress", "60% Progress") };
        var issueOnly = new WorkloadMeetingProjectRow { NextDeadline = new ProjectDeadline("A", Today, "IFC", "") };
        var none = new WorkloadMeetingProjectRow();

        Assert.Equal("Progress - 60% Progress", both.NextDeadlineWhat);
        Assert.Equal("IFC", issueOnly.NextDeadlineWhat);
        Assert.Equal("", none.NextDeadlineWhat);
        Assert.False(none.HasNextDeadline);
    }
}
