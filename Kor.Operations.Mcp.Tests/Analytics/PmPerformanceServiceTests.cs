#nullable enable
using Kor.Operations.PMTools;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Analytics;

public sealed class PmPerformanceServiceTests
{
    [Fact]
    public void Build_EmptyInput_ReturnsEmpty()
    {
        var rows = PmPerformanceService.Build([]);

        Assert.Empty(rows);
    }

    [Fact]
    public void Build_ComputesAggregatesAndScores()
    {
        var rows = PmPerformanceService.Build(
        [
            Project("PM1", fee: 1000, engHrs: 100, draftHrs: 0, estEngBudget: 110, arTotal: 100, ar90Plus: 0),
            Project("PM2", fee: 2000, engHrs: 100, draftHrs: 0, estEngBudget: 120, arTotal: 100, ar90Plus: 50),
            Project("PM3", fee: 3000, engHrs: 100, draftHrs: 0, estEngBudget: 140, arTotal: 0, ar90Plus: 0),
        ]);

        Assert.Equal(["PM3", "PM2", "PM1"], rows.Select(r => r.Pm).ToArray());

        var pm1 = rows.Single(r => r.Pm == "PM1");
        Assert.Equal(1000.0, pm1.TotalFee, 6);
        Assert.Equal(100.0, pm1.TotalEngHrs, 6);
        Assert.Equal(10.0, pm1.AvgEngDelta, 6);
        Assert.Equal(100.0, pm1.DeliveryHealthScore, 6);
        Assert.Equal(100.0, pm1.ArManagementScore, 6);
        Assert.Equal(80.0, pm1.PerformanceScore, 6);

        var pm2 = rows.Single(r => r.Pm == "PM2");
        Assert.Equal(65.0, pm2.PerformanceScore, 6);

        var pm3 = rows.Single(r => r.Pm == "PM3");
        Assert.Equal(70.0, pm3.PerformanceScore, 6);
    }

    [Fact]
    public void Build_ComputesRepeatClientCount()
    {
        var rows = PmPerformanceService.Build(
        [
            Project("Repeat", fee: 100, clientId: "A"),
            Project("Repeat", fee: 100, clientId: "A"),
            Project("Repeat", fee: 100, clientId: "B"),
            Project("Repeat", fee: 100, clientId: "C"),
            Project("Solo", fee: 50, clientId: "Z"),
        ]);

        var repeat = rows.Single(r => r.Pm == "Repeat");
        Assert.Equal(3, repeat.UniqueClients);
        Assert.Equal(1, repeat.RepeatClients);

        var solo = rows.Single(r => r.Pm == "Solo");
        Assert.Equal(1, solo.UniqueClients);
        Assert.Equal(0, solo.RepeatClients);
    }

    [Fact]
    public void Build_ReturnsRowsOrderedByTotalFeeDescending()
    {
        var rows = PmPerformanceService.Build(
        [
            Project("Low", fee: 100),
            Project("High", fee: 300),
            Project("Mid", fee: 200),
        ]);

        Assert.Equal(["High", "Mid", "Low"], rows.Select(r => r.Pm).ToArray());
    }

    private static HistoricalProjectRow Project(
        string pm,
        double fee,
        double engHrs = 10,
        double draftHrs = 0,
        double estEngBudget = 10,
        double arTotal = 0,
        double ar90Plus = 0,
        string clientId = "")
        => new()
        {
            Wbs1 = Guid.NewGuid().ToString("N"),
            Pm = pm,
            Fee = fee,
            EngHrs = engHrs,
            DraftHrs = draftHrs,
            EstEngBudget = estEngBudget,
            ArTotal = arTotal,
            Ar90Plus = ar90Plus,
            ClientId = clientId,
        };
}
