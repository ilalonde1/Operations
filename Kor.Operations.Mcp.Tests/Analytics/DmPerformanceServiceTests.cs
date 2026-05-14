#nullable enable
using Kor.Operations.PMTools;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Analytics;

public sealed class DmPerformanceServiceTests
{
    [Fact]
    public void Build_GroupsByDraftingManagerNotProjectManager()
    {
        var rows = DmPerformanceService.Build(
        [
            new HistoricalProjectRow
            {
                Wbs1 = "P1",
                Pm = "Alice",
                DraftingManager = "Bob",
                Fee = 1000,
                EngHrs = 10,
                EstEngBudget = 10,
            },
        ]);

        var row = Assert.Single(rows);
        Assert.Equal("Bob", row.Pm);
    }
}
