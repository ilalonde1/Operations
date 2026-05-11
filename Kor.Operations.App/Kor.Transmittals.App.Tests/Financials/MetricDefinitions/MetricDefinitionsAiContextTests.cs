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

    [Fact]
    public void TryGetAiMethodology_EmitsRelatedKpis_WhenRelationshipDefined()
    {
        // Batch 68: Billed_Margin's Description names Net and Revenue as
        // numerator / denominator. The methodology block should surface
        // both via a "Related KPIs" line — so AI explaining a margin
        // drop knows whether to look at Net moving or Revenue moving.
        var result = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("Billed_Margin");
        Assert.NotNull(result);
        Assert.Contains("Related KPIs", result, System.StringComparison.Ordinal);
        Assert.Contains("Billed_Net", result, System.StringComparison.Ordinal);
        Assert.Contains("Billed_Revenue", result, System.StringComparison.Ordinal);
        Assert.Contains("numerator", result, System.StringComparison.Ordinal);
        Assert.Contains("denominator", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetAiMethodology_OmitsRelatedKpisLine_WhenNoRelationshipDefined()
    {
        // Cash Position is intentionally standalone — no dictionary entry
        // ties it to another KPI. The methodology block should NOT emit a
        // "Related KPIs:" line for it, because there's nothing to point at.
        var result = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("Exec_CashPosition");
        Assert.NotNull(result);
        Assert.DoesNotContain("Related KPIs", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetAiMethodology_EmitsBidirectionalPair_ForEarnedVsInvoiced()
    {
        // Earned and Invoiced point at each other — the unbilled gap is
        // the spread. Either direction should surface its counterpart.
        var earned = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("Exec_Revenue3090");
        var invoiced = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("Exec_Billed3090");
        Assert.NotNull(earned);
        Assert.NotNull(invoiced);
        Assert.Contains("Exec_Billed3090", earned, System.StringComparison.Ordinal);
        Assert.Contains("Exec_Revenue3090", invoiced, System.StringComparison.Ordinal);
        Assert.Contains("UnbilledGap", earned, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetAiMethodology_RelatedKpiUsesDisplayName_NotJustKey()
    {
        // The "Related KPIs" line should be readable: it shows the display
        // name first ("Labor Margin (T12mo)"), then the key in parens, then
        // the note. AI should be able to mention the human label without
        // having to map keys → names itself.
        var result = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("Exec_NetMultiplier");
        Assert.NotNull(result);
        Assert.Contains("Labor Margin (T12mo)", result, System.StringComparison.Ordinal);
        Assert.Contains("(Exec_NetProfit)", result, System.StringComparison.Ordinal);
    }
}
