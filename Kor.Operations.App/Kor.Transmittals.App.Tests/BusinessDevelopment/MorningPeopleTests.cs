#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.MorningBrief;
using Kor.Operations.Compensation;
using Kor.Operations.Financials;
using Kor.Operations.PMTools;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// The COO people facts: twelve-week billable hours per person with the drop and overload situations, billed $ per person
/// from compensation rows, and PM workload from snapshot rows. Pure formatting only: does not read Deltek, check the
/// Compensation service's revenue allocation, or the delivery-confidence levels (fixtures leave them at the default).
/// </summary>
public sealed class MorningPeopleTests
{
    private static readonly DateTime Last = new(2026, 10, 11);

    private static EmployeeWeeklyHours Week(string id, string name, int weeksBack, double billable, double total) =>
        new() { EmployeeId = id, EmployeeName = name, WeekStart = Last.AddDays(-7 * weeksBack), BillableHrs = billable, TotalHrs = total };

    [Fact]
    public void BillableHours_RanksTwelveWeeks_AndIgnoresOlderWeeks()
    {
        var rows = Enumerable.Range(0, 12).Select(w => Week("E1", "Ada", w, 30, 40))
            .Concat(Enumerable.Range(0, 6).Select(w => Week("E2", "Ben", w, 35, 38)))
            .Append(Week("E2", "Ben", 12, 1000, 1000))   // week 13: outside the window
            .ToArray();
        var fact = MorningPeople.BillableHours(rows, Last);

        Assert.Equal(2, fact.Shown);
        Assert.StartsWith("Ada (E1): last week 30.0 of 40.0 h billable; 12 weeks 360 billable of 480 recorded (75%), 30.0 billable h/week, workload 107%; weeks with hours 12 of 12.", fact.Lines[1], StringComparison.Ordinal);
        Assert.StartsWith("Ben (E2): last week 35.0 of 38.0 h billable; 12 weeks 210 billable", fact.Lines[2], StringComparison.Ordinal);
        Assert.Contains("weeks with hours 6 of 12", fact.Lines[2], StringComparison.Ordinal);
        Assert.Equal("Everyone: 570 billable of 708 recorded hours (81%).", fact.Lines[^1]);
        Assert.Empty(fact.Situations);
    }

    [Fact]
    public void BillableHours_RaisesADroppedShare_AndASustainedOverload()
    {
        var drop = Enumerable.Range(0, 12).Select(w => Week("E1", "Ada", w, w < 4 ? 8 : 32, 40));
        var load = Enumerable.Range(0, 10).Select(w => Week("E2", "Ben", w, 40, 50));
        var fact = MorningPeople.BillableHours(drop.Concat(load).ToArray(), Last);

        Assert.Equal(new[] { "sustained overload", "billable drop" }, fact.Situations.Select(s => s.Kind).OrderByDescending(k => k));
        var d = fact.Situations.Single(s => s.Kind == "billable drop");
        Assert.Equal("A staff member's billable share fell from 80% to 20% over the last four weeks.", d.Query);
        Assert.DoesNotContain("Ada", d.Query, StringComparison.Ordinal);
        Assert.Contains("Ada", d.Printed, StringComparison.Ordinal);
    }

    [Fact]
    public void Billed_RanksByRevenue_MarksPartners_AndHandlesNoBillableHours()
    {
        var rows = new[]
        {
            new EmployeeCompensationRow { EmployeeId = "E7", FirstName = "Cy", LastName = "Low", AnnualizedRevenueActual = 0, AnnualizedHoursActual = 100 },
            new EmployeeCompensationRow { EmployeeId = "P0002", FirstName = "Jo", LastName = "Top", AnnualizedRevenueActual = 400_000,
                AnnualizedBillableHoursActual = 1_600, AnnualizedHoursActual = 2_000, UtilizationActual = .8, OvertimeHoursActual = 12 },
        };
        var fact = MorningPeople.Billed(rows);

        Assert.Equal("Jo Top (partner): billed $400,000; 1,600 billable of 2,000 recorded h (80%); $250 per billable h; overtime 12 h.", fact.Lines[1]);
        Assert.Contains("Cy Low: billed $0;", fact.Lines[2], StringComparison.Ordinal);
        Assert.Contains("no per billable h", fact.Lines[2], StringComparison.Ordinal);
        Assert.Equal("Everyone: billed $400,000; overtime 12 h.", fact.Lines[^1]);
    }

    [Fact]
    public void PmWorkload_GroupsByPm_AndOrdersByUnbilledFee()
    {
        static UtilizationRow Row(string pm, double fee, double billed, double budget, double hours) => UtilizationRow.FromProject(
            new FinancialsProjectRow { Wbs1 = pm + fee, Pm = pm, Fee = fee, FeeBilled = billed, EngBudget = budget, EngHrs = hours });
        var fact = MorningPeople.PmWorkload(new[]
        {
            Row("Ann", 100_000, 90_000, 100, 150), Row("Ann", 50_000, 0, 100, 10),
            Row("Bo", 500_000, 100_000, 1_000, 200), Row(" ", 1, 0, 0, 0),
        });

        Assert.Equal(2, fact.Shown);
        Assert.StartsWith("Bo: 1 projects; fee $500,000, unbilled $400,000; engineering 200 of 1,000 budgeted h;", fact.Lines[1], StringComparison.Ordinal);
        Assert.StartsWith("Ann: 2 projects; fee $150,000, unbilled $60,000; engineering 160 of 200 budgeted h;", fact.Lines[2], StringComparison.Ordinal);
        Assert.EndsWith("1 over budget.", fact.Lines[2], StringComparison.Ordinal);
    }
}
