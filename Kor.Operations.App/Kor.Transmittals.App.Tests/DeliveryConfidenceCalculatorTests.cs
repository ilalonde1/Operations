#nullable enable
using Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class DeliveryConfidenceCalculatorTests
{
    // ── Critical ─────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenHoursSpentExceedsBudget_ReturnsCritical()
    {
        var row = new FinancialsProjectRow
        {
            Fee = 100_000, FeeBilled = 50_000,
            EngBudget = 100, EngHrs = 110,  // over
            DraftBudget = 0,
        };

        var result = DeliveryConfidenceCalculator.Compute(row);

        Assert.Equal("Critical", result.Status);
        Assert.Equal("Red", result.ColorName);
    }

    [Fact]
    public void Compute_WhenFeeBilledExceedsFee_ReturnsCritical()
    {
        var row = new FinancialsProjectRow
        {
            Fee = 100_000, FeeBilled = 110_000,  // over
            EngBudget = 200, EngHrs = 50,
            DraftBudget = 200,
        };

        var result = DeliveryConfidenceCalculator.Compute(row);

        Assert.Equal("Critical", result.Status);
        Assert.Equal("Red", result.ColorName);
    }

    // ── At Risk ───────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenBurnFarAheadOfBilling_ReturnsAtRisk()
    {
        // pctHoursSpent = 80%, pctFeeBilled = 40% → gap = 40% > 15%
        var row = new FinancialsProjectRow
        {
            Fee = 100_000, FeeBilled = 40_000,
            EngBudget = 100, EngHrs = 80,
            DraftBudget = 0,
        };

        var result = DeliveryConfidenceCalculator.Compute(row);

        Assert.Equal("At Risk", result.Status);
        Assert.Equal("Orange", result.ColorName);
    }

    // ── Watch ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenRemainingEngBelowThreshold_ReturnsWatch()
    {
        // EngBudget = 100, EngHrs = 92 → remaining = 8 < 15 (15% of 100)
        // pctHoursSpent = 92/100 = 92%, pctFeeBilled = 90% → gap = 2% ≤ 15% (not at risk)
        var row = new FinancialsProjectRow
        {
            Fee = 100_000, FeeBilled = 90_000,
            EngBudget = 100, EngHrs = 92,
            DraftBudget = 0,
        };

        var result = DeliveryConfidenceCalculator.Compute(row);

        Assert.Equal("Watch", result.Status);
        Assert.Equal("Yellow", result.ColorName);
    }

    // ── High Confidence ───────────────────────────────────────────────────────

    [Fact]
    public void Compute_WhenEverythingOnTrack_ReturnsHighConfidence()
    {
        // pctHoursSpent = 40%, pctFeeBilled = 50% → gap = -10% (burn behind billing — healthy)
        // remainingEng = 60, threshold = 20 → 60 > 20 (not watch)
        var row = new FinancialsProjectRow
        {
            Fee = 100_000, FeeBilled = 50_000,
            EngBudget = 100, EngHrs = 40,
            DraftBudget = 0,
        };

        var result = DeliveryConfidenceCalculator.Compute(row);

        Assert.Equal("High Confidence", result.Status);
        Assert.Equal("Green", result.ColorName);
    }

    [Fact]
    public void Compute_WithNullRow_ReturnsHighConfidence()
    {
        var result = DeliveryConfidenceCalculator.Compute(null);

        Assert.Equal("High Confidence", result.Status);
    }

    [Fact]
    public void Compute_WithZeroBudgetAndFee_ReturnsHighConfidence()
    {
        var result = DeliveryConfidenceCalculator.Compute(new FinancialsProjectRow());

        Assert.Equal("High Confidence", result.Status);
    }

    // ── Tooltip always populated ──────────────────────────────────────────────

    [Theory]
    [InlineData(50_000, 90_000, 100, 95, 0)]   // Critical (hours)
    [InlineData(100_000, 40_000, 100, 80, 0)]  // At Risk
    [InlineData(100_000, 90_000, 100, 92, 0)]  // Watch
    [InlineData(100_000, 50_000, 100, 40, 0)]  // High Confidence
    public void Compute_TooltipIsNeverEmpty(double fee, double feeBilled, double engBudget, double engHrs, double draftBudget)
    {
        var row = new FinancialsProjectRow
        {
            Fee = fee, FeeBilled = feeBilled,
            EngBudget = engBudget, EngHrs = engHrs,
            DraftBudget = draftBudget,
        };

        var result = DeliveryConfidenceCalculator.Compute(row);

        Assert.False(string.IsNullOrWhiteSpace(result.Tooltip));
    }
}
