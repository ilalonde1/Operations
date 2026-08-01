#nullable enable
using Kor.Operations.PMTools;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Analytics;

public sealed class PerformanceScoringTests
{
    [Fact]
    public void ScorePmDmGroups_ComputesPercentilesAndComposite()
    {
        var rows = new List<PmPerformanceSummaryRow>
        {
            PmRow(avgEngDelta: 0.10, feePerHour: 100),
            PmRow(avgEngDelta: 0.20, feePerHour: 200),
            PmRow(avgEngDelta: 0.40, feePerHour: 300),
        };

        PerformanceScoring.ScorePmDmGroups(rows);

        Assert.Equal(100.0, rows[0].EstimationAccuracyScore, 6);
        Assert.Equal(50.0, rows[1].EstimationAccuracyScore, 6);
        Assert.Equal(0.0, rows[2].EstimationAccuracyScore, 6);
        Assert.Equal(0.0, rows[0].RevenueEfficiencyScore, 6);
        Assert.Equal(50.0, rows[1].RevenueEfficiencyScore, 6);
        Assert.Equal(100.0, rows[2].RevenueEfficiencyScore, 6);
        Assert.Equal(74.0, rows[0].PerformanceScore, 6);
        Assert.Equal(69.0, rows[1].PerformanceScore, 6);
        Assert.Equal(64.0, rows[2].PerformanceScore, 6);
    }

    [Fact]
    public void ScoreEmployeesSecondPass_ComputesEfficiencyProductivityAndConsistency()
    {
        var rows = new List<EmployeeSummaryRow>
        {
            Employee("E1", feePerHour: 100, constructionType: "Tower"),
            Employee("E2", feePerHour: 200, constructionType: "Tower"),
            Employee("E3", feePerHour: 300, constructionType: "Tower"),
            Employee("E4", feePerHour: 400, constructionType: "Retail"),
        };
        var hours = new List<EmployeeProjectHours>
        {
            ProjectHours("E1", "P1", 10),
            ProjectHours("E1", "P2", 20),
            ProjectHours("E1", "P3", 30),
        };

        PerformanceScoring.ScoreEmployeesSecondPass(rows, hours, wbs1 => wbs1 is "P1" or "P2" or "P3");

        Assert.Equal(0.0, rows[0].EfficiencyScore, 6);
        Assert.Equal(100.0 / 3.0, rows[1].EfficiencyScore, 6);
        Assert.Equal(200.0 / 3.0, rows[2].EfficiencyScore, 6);
        Assert.Equal(100.0, rows[3].EfficiencyScore, 6);
        Assert.Equal(39.0, rows[0].ProductivityScore, 6);
        Assert.Equal(52.0, rows[1].ProductivityScore, 6);
        Assert.Equal(66.0, rows[2].ProductivityScore, 6);
        Assert.Equal(79.0, rows[3].ProductivityScore, 6);
        Assert.Equal(0.408248290463863, rows[0].ConsistencyScore, 12);
    }

    [Fact]
    public void ScoreEmployeesSecondPass_SingleRowKeepsMedianEfficiencyDefault()
    {
        var rows = new List<EmployeeSummaryRow> { Employee("E1", feePerHour: 250, constructionType: "") };

        PerformanceScoring.ScoreEmployeesSecondPass(rows, [], _ => true);

        Assert.Equal(50.0, rows[0].EfficiencyScore, 6);
        Assert.Equal(59.0, rows[0].ProductivityScore, 6);
    }

    [Fact]
    public void ScoreMethods_EmptyInputs_DoNotThrow()
    {
        PerformanceScoring.ScorePmDmGroups([]);
        PerformanceScoring.ScoreEmployeesSecondPass([], [], _ => true);
        PerformanceScoring.ScoreEmployeesBackfillPass([]);
    }

    [Fact]
    public void ScoreEmployeesSecondPass_ComputesPeerComparison()
    {
        var rows = new List<EmployeeSummaryRow>
        {
            Employee("E1", feePerHour: 100, constructionType: "Tower"),
            Employee("E2", feePerHour: 200, constructionType: "Tower"),
            Employee("E3", feePerHour: 300, constructionType: "Tower"),
            Employee("E4", feePerHour: 400, constructionType: "Retail"),
        };

        PerformanceScoring.ScoreEmployeesSecondPass(rows, [], _ => true);

        Assert.Equal(250.0, rows[0].PeerGroupMedianFeePerHr, 6);
        Assert.Equal(40.0, rows[0].VsPeerPct, 6);
        Assert.Equal(2, rows[0].PeerCount);
        Assert.Equal(200.0, rows[1].PeerGroupMedianFeePerHr, 6);
        Assert.Equal(100.0, rows[1].VsPeerPct, 6);
        Assert.Equal(2, rows[1].PeerCount);
        Assert.Equal(0.0, rows[3].PeerGroupMedianFeePerHr, 6);
        Assert.Equal(0, rows[3].PeerCount);
    }

    [Fact]
    public void ScoreEmployeesBackfillPass_MatchesSecondPassForEfficiencyAndProductivity()
    {
        var secondPassRows = new List<EmployeeSummaryRow>
        {
            Employee("E1", feePerHour: 100, constructionType: ""),
            Employee("E2", feePerHour: 200, constructionType: ""),
            Employee("E3", feePerHour: 300, constructionType: ""),
        };
        var backfillRows = secondPassRows
            .Select(r => Employee(r.EmployeeId, r.FeePerHr, constructionType: ""))
            .ToList();

        PerformanceScoring.ScoreEmployeesSecondPass(secondPassRows, [], _ => true);
        PerformanceScoring.ScoreEmployeesBackfillPass(backfillRows);

        for (var i = 0; i < secondPassRows.Count; i++)
        {
            Assert.Equal(secondPassRows[i].EfficiencyScore, backfillRows[i].EfficiencyScore, 6);
            Assert.Equal(secondPassRows[i].ProductivityScore, backfillRows[i].ProductivityScore, 6);
        }
    }

    [Fact]
    public void Median_HandlesEmptyOddAndEvenInputs()
    {
        Assert.Equal(0.0, PerformanceScoring.Median([]), 6);
        Assert.Equal(3.0, PerformanceScoring.Median([5.0, 1.0, 3.0]), 6);
        Assert.Equal(4.0, PerformanceScoring.Median([8.0, 2.0, 6.0, 2.0]), 6);
    }

    private static PmPerformanceSummaryRow PmRow(double avgEngDelta, double feePerHour)
        => new()
        {
            TotalFee = feePerHour * 10.0,
            TotalEngHrs = 10.0,
            AvgEngDelta = avgEngDelta,
            DeliveryHealthScore = 80.0,
            ArManagementScore = 100.0,
        };

    private static EmployeeSummaryRow Employee(string id, double feePerHour, string constructionType)
        => new()
        {
            EmployeeId = id,
            TotalEngHrs = 10.0,
            AttributedFee = feePerHour * 10.0,
            BillableRateScore = 50.0,
            ProjectHealthScore = 80.0,
            PrimaryConstructionType = constructionType,
        };

    private static EmployeeProjectHours ProjectHours(string employeeId, string wbs1, double hours)
        => new()
        {
            EmployeeId = employeeId,
            Wbs1 = wbs1,
            EngHrs = hours,
            DraftHrs = 0.0,
        };
}
