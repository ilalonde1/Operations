#nullable enable
using Kor.Operations.PMTools;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Analytics;

public sealed class YearTrendServiceTests
{
    [Fact]
    public void Build_EmptyInput_ReturnsEmptyList()
    {
        var result = YearTrendService.Build(Array.Empty<HistoricalProjectRow>());
        Assert.Empty(result);
    }

    [Fact]
    public void Build_SkipsRowsWithoutOpenDate()
    {
        var rows = new[]
        {
            new HistoricalProjectRow { Wbs1 = "A", Fee = 1000, OpenDate = new DateTime(2024, 1, 1) },
            new HistoricalProjectRow { Wbs1 = "B", Fee = 2000, OpenDate = null },
        };

        var result = YearTrendService.Build(rows);

        Assert.Single(result);
        Assert.Equal(2024, result[0].Year);
        Assert.Equal(1, result[0].ProjectCount);
    }

    [Fact]
    public void Build_GroupsByOpenYearAndAggregatesCorrectly()
    {
        var rows = new[]
        {
            new HistoricalProjectRow
            {
                Wbs1 = "A", Fee = 100_000, OpenDate = new DateTime(2024, 3, 1),
                EngHrs = 400, DraftHrs = 100, TotalAllHrs = 800, BillableHrs = 600,
                SubCost = 10_000, AdminHrs = 100, NonBillHrs = 100, ArTotal = 5_000,
            },
            new HistoricalProjectRow
            {
                Wbs1 = "B", Fee = 200_000, OpenDate = new DateTime(2024, 6, 1),
                EngHrs = 800, DraftHrs = 200, TotalAllHrs = 1_600, BillableHrs = 1_200,
                SubCost = 20_000, AdminHrs = 200, NonBillHrs = 200, ArTotal = 10_000,
            },
            new HistoricalProjectRow
            {
                Wbs1 = "C", Fee = 50_000, OpenDate = new DateTime(2023, 1, 1),
                EngHrs = 200, DraftHrs = 50, TotalAllHrs = 400, BillableHrs = 300,
                SubCost = 5_000, AdminHrs = 50, NonBillHrs = 50, ArTotal = 2_500,
            },
        };

        var result = YearTrendService.Build(rows);

        Assert.Equal(2, result.Count);
        // Sorted descending by year
        Assert.Equal(2024, result[0].Year);
        Assert.Equal(2023, result[1].Year);

        var y2024 = result[0];
        Assert.Equal(2, y2024.ProjectCount);
        Assert.Equal(300_000, y2024.TotalFee);
        Assert.Equal(150_000, y2024.AvgFee);
        // Total production hours 2024: (400+100) + (800+200) = 1500
        Assert.Equal(300_000.0 / 1500, y2024.AvgFeePerHr, 6);
        // Net fee/hr: (300000 - 30000) / 1500 = 180
        Assert.Equal(180.0, y2024.AvgNetFeePerHr, 6);
        // Weighted eng% = 1200 / 1500 = 0.8
        Assert.Equal(0.8, y2024.WeightedEngPct, 6);
        // Total all hrs 2024 = 800 + 1600 = 2400; billable 600+1200 = 1800
        Assert.Equal(1800.0 / 2400, y2024.WeightedBillablePct, 6);
        // SubPct = 30000 / 300000 = 0.10
        Assert.Equal(0.10, y2024.AvgSubPct, 6);
        // Overhead = (100+100) + (200+200) = 600; ratio = 600/2400 = 0.25
        Assert.Equal(0.25, y2024.WeightedOverheadRatio, 6);
        Assert.Equal(15_000, y2024.TotalArOutstanding);
        // No firm utilization passed in
        Assert.Equal(0, y2024.FirmBillablePct);
    }

    [Fact]
    public void Build_WithFirmUtilization_PopulatesFirmBillablePctByYear()
    {
        var rows = new[]
        {
            new HistoricalProjectRow { Wbs1 = "A", Fee = 1000, OpenDate = new DateTime(2024, 1, 1) },
            new HistoricalProjectRow { Wbs1 = "B", Fee = 1000, OpenDate = new DateTime(2023, 1, 1) },
        };
        var firm = new FirmUtilizationStats
        {
            ByYear = new Dictionary<int, (double Total, double Billable)>
            {
                [2024] = (1000, 750),
                [2023] = (1000, 600),
            },
        };

        var result = YearTrendService.Build(rows, firm);

        Assert.Equal(0.75, result[0].FirmBillablePct, 6);   // 2024
        Assert.Equal(0.60, result[1].FirmBillablePct, 6);   // 2023
    }
}
