#nullable enable
using System.Reflection;
using System.Runtime.CompilerServices;
using AppFin = Kor.Operations.Financials;
using AppSvc = Kor.Operations.Services;
using Xunit;

namespace Kor.Operations.Tests.Financials.Executive;

/// <summary>
/// End-to-end check that ExecutiveSummaryViewModel.BuildContext() (the
/// snapshot AppAiService captures on every question) now emits the
/// FinancialMetricDefinitions methodology block alongside each KPI value.
/// This is the seam that takes AI from "guesses at industry-standard
/// formulas" to "cites KOR's actual methodology" — see project memory
/// on the dictionary-in-context bridge.
/// </summary>
public sealed class ExecutiveSummaryAiContextDefinitionsTests
{
    [Fact]
    public void BuildContext_IncludesMethodologyBlock_WhenKpiTitleMapsToDictionary()
    {
        var vm = VmWithKpi(new AppFin.ExecutiveKpi(
            Title: "Cash Position",
            ValueText: "$1.2M",
            SubText: "",
            StatusMessage: ""));

        string ctx = ((AppSvc.IAiContextProvider)vm).BuildContext();

        Assert.Contains("Cash Position", ctx, System.StringComparison.Ordinal);
        Assert.Contains("KPI methodology", ctx, System.StringComparison.Ordinal);
        Assert.Contains("(Exec_CashPosition)", ctx, System.StringComparison.Ordinal);
        Assert.Contains("How:", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContext_OmitsMethodologyBlock_WhenNoKpiTitleMapsToDictionary()
    {
        var vm = VmWithKpi(new AppFin.ExecutiveKpi(
            Title: "Made-Up KPI Not In Dictionary",
            ValueText: "—",
            SubText: "",
            StatusMessage: ""));

        string ctx = ((AppSvc.IAiContextProvider)vm).BuildContext();

        Assert.DoesNotContain("KPI methodology", ctx, System.StringComparison.Ordinal);
    }

    private static AppFin.ExecutiveSummaryViewModel VmWithKpi(AppFin.ExecutiveKpi kpi)
    {
        var service = (AppFin.ExecutiveSummaryService)RuntimeHelpers.GetUninitializedObject(
            typeof(AppFin.ExecutiveSummaryService));
        var vm = new AppFin.ExecutiveSummaryViewModel(service);
        var result = new AppFin.ExecutiveSummaryResult(
            GeneratedAt: new System.DateTimeOffset(2026, 5, 10, 9, 0, 0, System.TimeSpan.Zero),
            Kpis: new List<AppFin.ExecutiveKpi> { kpi },
            Trends: new List<AppFin.ExecutiveTrend>(),
            Alerts: new List<AppFin.ExecutiveAlert>(),
            MaxPostedPeriod: new System.DateTime(2026, 4, 1));

        typeof(AppFin.ExecutiveSummaryViewModel)
            .GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { result });

        return vm;
    }
}
