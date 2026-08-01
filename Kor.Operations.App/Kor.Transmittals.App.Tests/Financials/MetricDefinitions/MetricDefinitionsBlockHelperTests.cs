#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.MetricDefinitions;

/// <summary>
/// Locks the contract of <see cref="AppFin.FinancialMetricDefinitions.BuildAiMethodologyBlock"/>,
/// the helper Batch 62 introduced so VMs whose headline numbers aren't a
/// dynamic Kpis collection (Billed P&amp;L, GL P&amp;L, the Active-projects
/// view) can emit methodology with a fixed key list. Plus the existence of
/// the new Billed_* dictionary entries added in the same batch.
/// </summary>
public sealed class MetricDefinitionsBlockHelperTests
{
    [Fact]
    public void BuildAiMethodologyBlock_ReturnsNull_WhenNoKeysMatch()
    {
        var block = AppFin.FinancialMetricDefinitions.BuildAiMethodologyBlock(
            new[] { "Bogus_1", "Bogus_2" });
        Assert.Null(block);
    }

    [Fact]
    public void BuildAiMethodologyBlock_FormatsKnownKey_WithDisplayNameAndHowLine()
    {
        var block = AppFin.FinancialMetricDefinitions.BuildAiMethodologyBlock(
            new[] { "Exec_CashPosition" });
        Assert.NotNull(block);
        Assert.Contains("Cash Position (Exec_CashPosition)", block, System.StringComparison.Ordinal);
        Assert.Contains("How:", block, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAiMethodologyBlock_SkipsUnknownKeys_KeepsKnownOnes()
    {
        var block = AppFin.FinancialMetricDefinitions.BuildAiMethodologyBlock(
            new[] { "Bogus_1", "Exec_CashPosition", "Bogus_2" });
        Assert.NotNull(block);
        Assert.Contains("Exec_CashPosition", block, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Bogus_1", block, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Bogus_2", block, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAiMethodologyBlock_PreservesKeyOrder()
    {
        var block = AppFin.FinancialMetricDefinitions.BuildAiMethodologyBlock(
            new[] { "Exec_NetMultiplier", "Exec_CashPosition" });
        Assert.NotNull(block);
        var posMult = block.IndexOf("Exec_NetMultiplier", System.StringComparison.Ordinal);
        var posCash = block.IndexOf("Exec_CashPosition", System.StringComparison.Ordinal);
        Assert.True(posMult > 0 && posCash > 0);
        Assert.True(posMult < posCash, "Order requested must be preserved");
    }

    // ── New Billed_* dictionary entries shipped in Batch 62 ──

    [Theory]
    [InlineData("Billed_Revenue",        "Billed Revenue")]
    [InlineData("Billed_Expenses",       "Billed Expenses")]
    [InlineData("Billed_Net",            "Billed Net Income")]
    [InlineData("Billed_Margin",         "Billed Margin")]
    [InlineData("Billed_Reconciliation", "Billed vs Posted GL Reconciliation")]
    public void BilledPnL_DictionaryEntry_ExistsWithExpectedDisplayName(string key, string expectedDisplayName)
    {
        Assert.True(AppFin.FinancialMetricDefinitions.Definitions.ContainsKey(key),
            $"Definition for {key} missing — Definitions.BilledPnL.cs not wired into BuildDefinitions?");
        Assert.Equal(expectedDisplayName,
            AppFin.FinancialMetricDefinitions.Definitions[key].DisplayName);
    }

    [Fact]
    public void BilledRevenue_Methodology_CitesLedgerArAndCanonicalAccounts()
    {
        // The whole reason Billed_Revenue exists as a dictionary entry:
        // AI should cite LedgerAR + the canonical revenue-account prefix-match,
        // not invent a generic billed-revenue formula. Lock the key phrases.
        var block = AppFin.FinancialMetricDefinitions.TryGetAiMethodology("Billed_Revenue");
        Assert.NotNull(block);
        Assert.Contains("LedgerAR",   block, System.StringComparison.Ordinal);
        Assert.Contains("TransType",  block, System.StringComparison.Ordinal);
        Assert.Contains("4001",       block, System.StringComparison.Ordinal);
        Assert.Contains("4260",       block, System.StringComparison.Ordinal); // intercompany call-out
    }
}
