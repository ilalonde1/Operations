#nullable enable
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

public sealed class FxPlumbingTests
{
    [Fact]
    public void CashUsesCashUsdToCadRate_ArUsesBilledUsdToCadRate()
    {
        var deltek = SyntheticDeltekData.Default(
            cashCad: 100,
            cashUsa: 100,
            cashFx: 1.50,
            arFx: 1.30,
            arFirmwide: 230,
            arFirmwideCad: 100,
            arFirmwideUsa: 100);

        var result = ExecutiveSummaryTestSupport.Build(deltek: deltek);
        var cash = ExecutiveSummaryTestSupport.Kpi(result, "Cash Position");
        var liquidity = ExecutiveSummaryTestSupport.Kpi(result, "Liquidity (Cash + AR)");

        Assert.Contains("@ 1.50", cash.SubText, StringComparison.Ordinal);
        Assert.Contains("@ 1.30", liquidity.SubText, StringComparison.Ordinal);
    }
}
