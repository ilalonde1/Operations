#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

public sealed class AlignedFlagTests
{
    [Fact]
    public void RevenueTrend_AlignedWhenGapsZero_BadgeShown()
    {
        var result = ExecutiveSummaryTestSupport.Build(
            deltek: SyntheticDeltekData.Default(revenue30: 10_000, billed30: 10_000, revenue90: 30_000, billed90: 30_000));
        var trend = ExecutiveSummaryTestSupport.Trend(result, "Revenue (Earned) (latest 1 / 3 periods)");
        var vm = new AppFin.TrendCardVm(trend);

        Assert.True(trend.IsAligned);
        Assert.Equal("Aligned", vm.BadgeText);
        Assert.Equal(System.Windows.Visibility.Visible, vm.BadgeVisibility);
    }

    [Fact]
    public void RevenueTrend_NotAligned_BadgeHidden()
    {
        var result = ExecutiveSummaryTestSupport.Build(
            deltek: SyntheticDeltekData.Default(revenue30: 10_000, billed30: 8_000, revenue90: 30_000, billed90: 20_000));
        var trend = ExecutiveSummaryTestSupport.Trend(result, "Revenue (Earned) (latest 1 / 3 periods)");
        var vm = new AppFin.TrendCardVm(trend);

        Assert.False(trend.IsAligned);
        Assert.Equal("", vm.BadgeText);
        Assert.Equal(System.Windows.Visibility.Collapsed, vm.BadgeVisibility);
    }
}
