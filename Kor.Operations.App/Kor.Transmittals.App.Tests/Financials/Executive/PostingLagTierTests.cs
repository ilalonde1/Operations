#nullable enable
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

public sealed class PostingLagTierTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PostingLagSeverity_CollapsedAtLagOneOrLess(int lagMonths)
    {
        var vm = VmWithLag(lagMonths);

        Assert.Equal(Visibility.Collapsed, vm.PostingLagVisibility);
        Assert.Equal("", vm.PostingLagSeverity);
        Assert.Equal("", vm.PostingLagBanner);
    }

    [Fact]
    public void PostingLagSeverity_InfoAtLagTwo()
    {
        var vm = VmWithLag(2);

        Assert.Equal(Visibility.Visible, vm.PostingLagVisibility);
        Assert.Equal("Info", vm.PostingLagSeverity);
        Assert.Contains("normal close lag", vm.PostingLagBanner, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void PostingLagSeverity_WarningAtLagThreeOrMore(int lagMonths)
    {
        var vm = VmWithLag(lagMonths);

        Assert.Equal(Visibility.Visible, vm.PostingLagVisibility);
        Assert.Equal("Warning", vm.PostingLagSeverity);
        Assert.Contains($"{lagMonths}-month", vm.PostingLagBanner, StringComparison.OrdinalIgnoreCase);
    }

    private static AppFin.ExecutiveSummaryViewModel VmWithLag(int lagMonths)
    {
        var service = (AppFin.ExecutiveSummaryService)RuntimeHelpers.GetUninitializedObject(typeof(AppFin.ExecutiveSummaryService));
        var vm = new AppFin.ExecutiveSummaryViewModel(service);
        var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var posted = currentMonth.AddMonths(-lagMonths);
        var result = new AppFin.ExecutiveSummaryResult(
            GeneratedAt: new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero),
            Kpis: new List<AppFin.ExecutiveKpi>(),
            Trends: new List<AppFin.ExecutiveTrend>(),
            Alerts: new List<AppFin.ExecutiveAlert>(),
            MaxPostedPeriod: posted);

        typeof(AppFin.ExecutiveSummaryViewModel)
            .GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { result });

        return vm;
    }
}
