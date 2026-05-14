#nullable enable
using System.Reflection;
using AppPm = Kor.Operations.PMTools;
using Xunit;

namespace Kor.Operations.Tests.PMTools;

/// <summary>
/// Arc 5 (Batch 99) trimmed AnalyticsAiService.BuildContext to scope-only.
/// Off-screen data now comes from MCP tools (get_pm_performance,
/// get_employee_performance, get_project_detail, etc). This test locks the
/// trim so a future "while I'm here let me also dump ALL EMPLOYEES" cannot
/// silently re-bloat the prompt.
/// </summary>
public sealed class AnalyticsAiServiceTrimTests
{
    [Fact]
    public void BuildContext_DoesNotDumpBulkLists()
    {
        var vm = MakeEmptyVm();
        var ctx = InvokeBuildContext(vm);

        Assert.DoesNotContain("=== ALL EMPLOYEES ===", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("=== PROJECT MANAGERS ===", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("=== DRAFTING MANAGERS ===", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("=== OVER-BUDGET PROJECTS", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("=== PROJECTS (historical", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("=== EMPLOYEE WEEKLY UTILIZATION", ctx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("=== YEAR-OVER-YEAR TREND", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContext_EmitsPortfolioScopeAndToolPointers()
    {
        var vm = MakeEmptyVm();
        var ctx = InvokeBuildContext(vm);

        Assert.Contains("=== PORTFOLIO OVERVIEW (current filter) ===", ctx, System.StringComparison.Ordinal);
        Assert.Contains("=== OFF-SCREEN DATA — USE TOOLS ===", ctx, System.StringComparison.Ordinal);
        // Tool pointers for every Arc 1-4 tool.
        Assert.Contains("get_pm_performance", ctx, System.StringComparison.Ordinal);
        Assert.Contains("get_dm_performance", ctx, System.StringComparison.Ordinal);
        Assert.Contains("get_employee_performance", ctx, System.StringComparison.Ordinal);
        Assert.Contains("get_employee_utilization", ctx, System.StringComparison.Ordinal);
        Assert.Contains("get_project_detail", ctx, System.StringComparison.Ordinal);
        Assert.Contains("get_at_risk_projects", ctx, System.StringComparison.Ordinal);
        Assert.Contains("get_project_yoy_trend", ctx, System.StringComparison.Ordinal);
        Assert.Contains("get_firm_utilization_by_year", ctx, System.StringComparison.Ordinal);
        Assert.Contains("get_revenue_timeline", ctx, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContext_StaysSmall()
    {
        // Hard cap: the trimmed context for an empty ViewModel should be
        // under ~2 KB. Pre-trim, the same call could emit 30+ KB. If this
        // test fails, someone added a bulk dump back.
        var vm = MakeEmptyVm();
        var ctx = InvokeBuildContext(vm);
        Assert.True(ctx.Length < 2048, $"BuildContext is {ctx.Length} bytes; expected < 2048");
    }

    private static AppPm.HistoricalAnalyticsViewModel MakeEmptyVm()
    {
        // VM has an implicit parameterless constructor. Field initializers
        // run, but no data loads — perfect for a scope-only context test.
        return new AppPm.HistoricalAnalyticsViewModel();
    }

    private static string InvokeBuildContext(AppPm.HistoricalAnalyticsViewModel vm)
    {
        var method = typeof(AppPm.AnalyticsAiService)
            .GetMethod("BuildContext", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(null, new object[] { vm })!;
    }
}
