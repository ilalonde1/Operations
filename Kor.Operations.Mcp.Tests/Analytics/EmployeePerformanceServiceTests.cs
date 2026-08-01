#nullable enable
using Kor.Operations.PMTools;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Analytics;

public sealed class EmployeePerformanceServiceTests
{
    [Fact]
    public void Build_EmptyHours_ReturnsEmpty()
    {
        var rows = EmployeePerformanceService.Build([], [], []);

        Assert.Empty(rows);
    }

    [Fact]
    public void Build_FiltersExcludedEmployeeId()
    {
        var rows = EmployeePerformanceService.Build(
            [Project("P1", fee: 1000, engHrs: 10, constructionType: "Tower")],
            [Hours("EX", "P1", engHrs: 10, totalHrs: 10)],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EX" });

        Assert.Empty(rows);
    }

    [Fact]
    public void Build_AttributesFeeProportionallyByProjectProductionHours()
    {
        var rows = EmployeePerformanceService.Build(
            [Project("P1", fee: 1000, engHrs: 20, constructionType: "Tower")],
            [
                Hours("E1", "P1", engHrs: 10, totalHrs: 10),
                Hours("E2", "P1", draftHrs: 10, totalHrs: 10),
            ],
            []);

        Assert.Equal(2, rows.Count);
        Assert.Equal(500.0, rows.Single(r => r.EmployeeId == "E1").AttributedFee, 6);
        Assert.Equal(500.0, rows.Single(r => r.EmployeeId == "E2").AttributedFee, 6);
    }

    [Fact]
    public void Build_PrimaryConstructionTypeUsesMostProductionHours()
    {
        var rows = EmployeePerformanceService.Build(
            [
                Project("P1", fee: 1000, engHrs: 10, constructionType: "Tower"),
                Project("P2", fee: 1000, engHrs: 20, constructionType: "Retail"),
            ],
            [
                Hours("E1", "P1", engHrs: 5, totalHrs: 5),
                Hours("E1", "P2", engHrs: 20, totalHrs: 20),
            ],
            []);

        var row = Assert.Single(rows);
        Assert.Equal("Retail", row.PrimaryConstructionType);
    }

    [Fact]
    public void Build_ComputesProductivityCompositeThroughScoring()
    {
        var rows = EmployeePerformanceService.Build(
            [
                Project("P1", fee: 1000, engHrs: 10, constructionType: "Tower"),
                Project("P2", fee: 2000, engHrs: 10, constructionType: "Tower"),
            ],
            [
                Hours("E1", "P1", engHrs: 10, totalHrs: 10),
                Hours("E2", "P2", engHrs: 10, totalHrs: 10),
            ],
            []);

        var e1 = rows.Single(r => r.EmployeeId == "E1");
        var e2 = rows.Single(r => r.EmployeeId == "E2");

        Assert.Equal(100.0, e1.BillableRateScore, 6);
        Assert.Equal(100.0, e1.ProjectHealthScore, 6);
        Assert.Equal(0.0, e1.EfficiencyScore, 6);
        Assert.Equal(60.0, e1.ProductivityScore, 6);

        Assert.Equal(100.0, e2.BillableRateScore, 6);
        Assert.Equal(100.0, e2.ProjectHealthScore, 6);
        Assert.Equal(100.0, e2.EfficiencyScore, 6);
        Assert.Equal(100.0, e2.ProductivityScore, 6);
    }

    private static HistoricalProjectRow Project(string wbs1, double fee, double engHrs, string constructionType)
        => new()
        {
            Wbs1 = wbs1,
            Fee = fee,
            EngHrs = engHrs,
            EstEngBudget = 0,
            ConstructionType = constructionType,
        };

    private static EmployeeProjectHours Hours(
        string employeeId,
        string wbs1,
        double engHrs = 0,
        double draftHrs = 0,
        double totalHrs = 0)
        => new()
        {
            EmployeeId = employeeId,
            EmployeeName = employeeId,
            Wbs1 = wbs1,
            EngHrs = engHrs,
            DraftHrs = draftHrs,
            TotalHrs = totalHrs,
        };
}
