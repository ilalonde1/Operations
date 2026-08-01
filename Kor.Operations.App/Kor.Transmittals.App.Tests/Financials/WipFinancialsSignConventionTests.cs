#nullable enable
using Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials;

public sealed class WipFinancialsSignConventionTests
{
    [Fact]
    public void RevenueGreaterThanBilled_IsEarnedNotOverbilled()
    {
        const double revenue = 125_000.0;
        const double billed = 80_000.0;

        var split = WipFinancialsService.SplitWipNet(revenue - billed);

        Assert.Equal(45_000.0, split.Earned);
        Assert.Equal(0.0, split.Overbilled);
        Assert.Equal(45_000.0, split.Net);
    }

    [Fact]
    public void BilledGreaterThanRevenue_IsOverbilledNotEarned()
    {
        const double revenue = 80_000.0;
        const double billed = 125_000.0;

        var split = WipFinancialsService.SplitWipNet(revenue - billed);

        Assert.Equal(0.0, split.Earned);
        Assert.Equal(45_000.0, split.Overbilled);
        Assert.Equal(-45_000.0, split.Net);
    }

    [Fact]
    public void PositiveUnbilled_IsEarnedNotOverbilled()
    {
        const double unbilled = 45_000.0;

        var split = WipFinancialsService.SplitWipNet(unbilled);

        Assert.Equal(45_000.0, split.Earned);
        Assert.Equal(0.0, split.Overbilled);
        Assert.Equal(45_000.0, split.Net);
    }
}
