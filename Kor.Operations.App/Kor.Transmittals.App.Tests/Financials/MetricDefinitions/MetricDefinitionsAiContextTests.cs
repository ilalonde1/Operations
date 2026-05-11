#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.MetricDefinitions;

/// <summary>
/// Locks the contract of FinancialMetricDefinitions.TryGetAiMethodology
/// and TryResolveKeyFromDisplayName — the bridge the IAiContextProvider
/// implementations use to emit KOR's actual methodology (predicates,
/// FX handling, exclusions) alongside each KPI value. If these regress
/// the AI starts guessing industry-standard formulas again.
/// </summary>
public sealed class MetricDefinitionsAiContextTests
{
    [Fact]
    public void TryGetAiMethodology_ReturnsNull_ForUnknownKey()
    {
        var result = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("Bogus_Not_A_Real_Key");
        Assert.Null(result);
    }

    [Fact]
    public void TryGetAiMethodology_ReturnsNull_ForWhitespaceKey()
    {
        Assert.Null(AppFin.FinancialMetricDefinitions.TryGetAiMethodology(""));
        Assert.Null(AppFin.FinancialMetricDefinitions.TryGetAiMethodology("   "));
    }

    [Fact]
    public void TryGetAiMethodology_ReturnsHowSection_ForKnownKeyWithDescription()
    {
        // Exec_CashPosition has a multi-section description including
        // HOW IT IS CALCULATED. AI should see the "How:" line but NOT
        // the "WHY IT MATTERS:" prose (which is human-motivation noise).
        var result = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("Exec_CashPosition");
        Assert.NotNull(result);
        Assert.Contains("How:", result, System.StringComparison.Ordinal);
        Assert.DoesNotContain("WHY IT MATTERS", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetAiMethodology_IncludesFormula_WhenFormulaPopulated()
    {
        // Forecast_MonthsOfRunway has an explicit Formula = "Backlog / Baseline pace".
        var result = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("Forecast_MonthsOfRunway");
        Assert.NotNull(result);
        Assert.Contains("Formula:", result, System.StringComparison.Ordinal);
        Assert.Contains("Backlog", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetAiMethodology_SkipsPlaceholderFormula()
    {
        // The dictionary's NormalizeDefinitions step backfills any empty
        // Formula with "Calculation: see business definition." Anything
        // that surfaces THAT string into the AI context is noise — skip it.
        // Pick a key whose Formula was left empty (e.g., TotalFees in Core).
        var result = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("TotalFees");
        if (result == null) return; // Acceptable: no HOW section either.
        Assert.DoesNotContain("Calculation: see business definition.", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveKeyFromDisplayName_FindsKey_ForKnownDisplayName()
    {
        var key = AppFin.FinancialMetricDefinitions.TryResolveKeyFromDisplayName("Cash Position");
        Assert.Equal("Exec_CashPosition", key);
    }

    [Fact]
    public void TryResolveKeyFromDisplayName_ReturnsNull_ForUnknownDisplayName()
    {
        Assert.Null(AppFin.FinancialMetricDefinitions.TryResolveKeyFromDisplayName("Not A Real KPI"));
        Assert.Null(AppFin.FinancialMetricDefinitions.TryResolveKeyFromDisplayName(""));
        Assert.Null(AppFin.FinancialMetricDefinitions.TryResolveKeyFromDisplayName("   "));
    }

    [Fact]
    public void TryResolveKeyFromDisplayName_IsCaseInsensitive()
    {
        Assert.Equal("Exec_CashPosition",
            AppFin.FinancialMetricDefinitions.TryResolveKeyFromDisplayName("cash position"));
        Assert.Equal("Exec_CashPosition",
            AppFin.FinancialMetricDefinitions.TryResolveKeyFromDisplayName("CASH POSITION"));
    }

    [Fact]
    public void TryResolveKeyFromDisplayName_TrimsWhitespace()
    {
        Assert.Equal("Exec_CashPosition",
            AppFin.FinancialMetricDefinitions.TryResolveKeyFromDisplayName("  Cash Position  "));
    }
}
