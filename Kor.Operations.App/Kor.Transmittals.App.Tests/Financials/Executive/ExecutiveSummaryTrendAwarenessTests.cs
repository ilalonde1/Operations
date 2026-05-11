#nullable enable
using System.Reflection;
using System.Runtime.CompilerServices;
using AppFin = Kor.Operations.Financials;
using AppSvc = Kor.Operations.Services;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

/// <summary>
/// Locks the Batch 67 contract: ExecutiveSummaryViewModel.BuildContext()
/// must surface the on-screen sparkline values plus a start→end delta and
/// a direction word (rising / falling / flat) for every trend that has
/// at least two data points. Without these the AI can only repeat the
/// current snapshot — it can't answer the most common follow-up on a
/// financial dashboard ("is this getting better or worse?") without
/// firing a tool call.
/// </summary>
public sealed class ExecutiveSummaryTrendAwarenessTests
{
    [Fact]
    public void BuildContext_EmitsTrendValues_AndRisingDirection_WhenSeriesIncreases()
    {
        var vm = VmWithTrend(new AppFin.ExecutiveTrend(
            Title: "Net Multiplier",
            ValueText: "2.50",
            StatusMessage: "",
            Values: new[] { 2.30, 2.40, 2.45, 2.50 }));

        string ctx = ((AppSvc.IAiContextProvider)vm).BuildContext();

        Assert.Contains("Net Multiplier", ctx, System.StringComparison.Ordinal);
        Assert.Contains("oldest → newest", ctx, System.StringComparison.Ordinal);
        Assert.Contains("values: 2.3, 2.4, 2.45, 2.5", ctx, System.StringComparison.Ordinal);
        Assert.Contains("direction: rising", ctx, System.StringComparison.Ordinal);
        Assert.Contains("+8.7%", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContext_EmitsFallingDirection_WhenSeriesDecreases()
    {
        var vm = VmWithTrend(new AppFin.ExecutiveTrend(
            Title: "Cash Position",
            ValueText: "$1.0M",
            StatusMessage: "",
            Values: new[] { 1200.0, 1150.0, 1080.0, 1000.0 }));

        string ctx = ((AppSvc.IAiContextProvider)vm).BuildContext();

        Assert.Contains("direction: falling", ctx, System.StringComparison.Ordinal);
        Assert.Contains("-16.7%", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContext_EmitsFlatDirection_WhenSeriesIsFlat()
    {
        var vm = VmWithTrend(new AppFin.ExecutiveTrend(
            Title: "Utilization",
            ValueText: "62%",
            StatusMessage: "",
            Values: new[] { 0.620, 0.621, 0.625, 0.620 }));

        string ctx = ((AppSvc.IAiContextProvider)vm).BuildContext();

        Assert.Contains("direction: flat", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContext_OmitsValuesBlock_WhenTrendHasNoSeries()
    {
        // A trend can be present (e.g. headline + status) without an underlying
        // sparkline — Values may be null while ValueText is set. Don't crash,
        // don't emit a junk "values:" line.
        var vm = VmWithTrend(new AppFin.ExecutiveTrend(
            Title: "Booked Backlog",
            ValueText: "$8.4M",
            StatusMessage: "",
            Values: null));

        string ctx = ((AppSvc.IAiContextProvider)vm).BuildContext();

        Assert.Contains("Booked Backlog", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("values:", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("direction:", ctx, System.StringComparison.Ordinal);
    }

    private static AppFin.ExecutiveSummaryViewModel VmWithTrend(AppFin.ExecutiveTrend trend)
    {
        var service = (AppFin.ExecutiveSummaryService)RuntimeHelpers.GetUninitializedObject(
            typeof(AppFin.ExecutiveSummaryService));
        var vm = new AppFin.ExecutiveSummaryViewModel(service);
        var result = new AppFin.ExecutiveSummaryResult(
            GeneratedAt: new System.DateTimeOffset(2026, 5, 10, 9, 0, 0, System.TimeSpan.Zero),
            Kpis: new List<AppFin.ExecutiveKpi>(),
            Trends: new List<AppFin.ExecutiveTrend> { trend },
            Alerts: new List<AppFin.ExecutiveAlert>(),
            MaxPostedPeriod: new System.DateTime(2026, 4, 1));

        typeof(AppFin.ExecutiveSummaryViewModel)
            .GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { result });

        return vm;
    }
}
