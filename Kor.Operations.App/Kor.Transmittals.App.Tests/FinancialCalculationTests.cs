#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core;
using Kor.Operations.Financials;
using Kor.Operations.Shared;
using Xunit;
using static Kor.Operations.Shared.PeerBudgetEstimator;

namespace Kor.Operations.Tests;

public sealed class FinancialCalculationTests
{
    // ── SafeDiv ─────────────────────────────────────────────────────────

    [Fact]
    public void SafeDiv_Double_NormalDivision() =>
        Assert.Equal(2.5, MathHelpers.SafeDiv(5.0, 2.0));

    [Fact]
    public void SafeDiv_Double_ZeroDenominator() =>
        Assert.Equal(0.0, MathHelpers.SafeDiv(5.0, 0.0));

    [Fact]
    public void SafeDiv_Double_ZeroNumerator() =>
        Assert.Equal(0.0, MathHelpers.SafeDiv(0.0, 5.0));

    [Fact]
    public void SafeDiv_Double_BothZero() =>
        Assert.Equal(0.0, MathHelpers.SafeDiv(0.0, 0.0));

    [Fact]
    public void SafeDiv_Decimal_NormalDivision() =>
        Assert.Equal(2.5m, MathHelpers.SafeDiv(5.0m, 2.0m));

    [Fact]
    public void SafeDiv_Decimal_ZeroDenominator() =>
        Assert.Equal(0m, MathHelpers.SafeDiv(5.0m, 0m));

    // ── Median ──────────────────────────────────────────────────────────

    [Fact]
    public void Median_EmptyList_ReturnsZero() =>
        Assert.Equal(0.0, PeerBudgetEstimator.Median(new List<double>()));

    [Fact]
    public void Median_SingleValue() =>
        Assert.Equal(7.0, PeerBudgetEstimator.Median(new List<double> { 7.0 }));

    [Fact]
    public void Median_OddCount() =>
        Assert.Equal(5.0, PeerBudgetEstimator.Median(new List<double> { 3.0, 5.0, 9.0 }));

    [Fact]
    public void Median_EvenCount() =>
        Assert.Equal(4.0, PeerBudgetEstimator.Median(new List<double> { 2.0, 3.0, 5.0, 9.0 }));

    [Fact]
    public void Median_UnsortedInput_StillCorrect() =>
        Assert.Equal(5.0, PeerBudgetEstimator.Median(new List<double> { 9.0, 3.0, 5.0 }));

    [Fact]
    public void Median_DoesNotMutateInput()
    {
        var input = new List<double> { 9.0, 3.0, 5.0 };
        PeerBudgetEstimator.Median(input);
        Assert.Equal(9.0, input[0]);
    }

    // ── PeerBudgetEstimator.Estimate ────────────────────────────────────

    [Fact]
    public void Estimate_ZeroFee_ReturnsZero()
    {
        var peers = BuildPeers(10, 100_000, "Commercial", "CD", "SmallJobs");
        var (eng, draft, count) = PeerBudgetEstimator.Estimate(0, "CD", "Commercial", "SmallJobs", peers, "");
        Assert.Equal(0, count);
    }

    [Fact]
    public void Estimate_EmptyPeers_ReturnsZero()
    {
        var (eng, draft, count) = PeerBudgetEstimator.Estimate(100_000, "CD", "Commercial", "SmallJobs", new List<PeerProject>(), "");
        Assert.Equal(0, count);
    }

    [Fact]
    public void Estimate_FewerThanThreePeers_ReturnsZero()
    {
        var peers = BuildPeers(2, 100_000, "Commercial", "CD", "SmallJobs");
        var (eng, draft, count) = PeerBudgetEstimator.Estimate(100_000, "CD", "Commercial", "SmallJobs", peers, "");
        Assert.Equal(0, count);
    }

    [Fact]
    public void Estimate_ExactMatchPeers_ReturnsMedian()
    {
        var peers = new List<PeerProject>
        {
            new() { Wbs1 = "P1", Fee = 95_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 100, DraftHrs = 50 },
            new() { Wbs1 = "P2", Fee = 100_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 120, DraftHrs = 60 },
            new() { Wbs1 = "P3", Fee = 105_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 140, DraftHrs = 70 },
        };

        var (eng, draft, count) = PeerBudgetEstimator.Estimate(100_000, "CD", "Commercial", "SmallJobs", peers, "");

        Assert.Equal(3, count);
        Assert.Equal(120.0, eng);
        Assert.Equal(60.0, draft);
    }

    [Fact]
    public void Estimate_ExcludesCurrentProject()
    {
        var peers = new List<PeerProject>
        {
            new() { Wbs1 = "SELF", Fee = 100_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 999, DraftHrs = 999 },
            new() { Wbs1 = "P1", Fee = 95_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 100, DraftHrs = 50 },
            new() { Wbs1 = "P2", Fee = 100_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 120, DraftHrs = 60 },
            new() { Wbs1 = "P3", Fee = 105_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 140, DraftHrs = 70 },
        };

        var (eng, draft, count) = PeerBudgetEstimator.Estimate(100_000, "CD", "Commercial", "SmallJobs", peers, "SELF");

        Assert.Equal(3, count);
        Assert.Equal(120.0, eng);
    }

    [Fact]
    public void Estimate_WidensFeeRangeWhenNarrowFails()
    {
        var peers = new List<PeerProject>
        {
            new() { Wbs1 = "P1", Fee = 55_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 80, DraftHrs = 40 },
            new() { Wbs1 = "P2", Fee = 60_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 90, DraftHrs = 45 },
            new() { Wbs1 = "P3", Fee = 65_000, ConstructionType = "Commercial", Phase = "CD", ProjectCategory = "SmallJobs", EngHrs = 100, DraftHrs = 50 },
        };

        // Fee=100k, ±30% = 70k-130k → none match. ±50% = 50k-150k → all three match.
        var (eng, draft, count) = PeerBudgetEstimator.Estimate(100_000, "CD", "Commercial", "SmallJobs", peers, "");

        Assert.Equal(3, count);
        Assert.Equal(90.0, eng);
    }

    [Fact]
    public void Estimate_FallsBackToLessSpecificTier()
    {
        var peers = new List<PeerProject>
        {
            // Same type but different category/phase — won't match type+cat+phase or type+phase
            new() { Wbs1 = "P1", Fee = 95_000, ConstructionType = "Commercial", Phase = "SD", ProjectCategory = "Office", EngHrs = 100, DraftHrs = 50 },
            new() { Wbs1 = "P2", Fee = 100_000, ConstructionType = "Commercial", Phase = "DD", ProjectCategory = "Hotel", EngHrs = 120, DraftHrs = 60 },
            new() { Wbs1 = "P3", Fee = 105_000, ConstructionType = "Commercial", Phase = "CA", ProjectCategory = "SmallJobs", EngHrs = 140, DraftHrs = 70 },
        };

        // Requesting CD/Commercial/SmallJobs — no exact matches, but type-only pool has all 3
        var (eng, draft, count) = PeerBudgetEstimator.Estimate(100_000, "CD", "Commercial", "SmallJobs", peers, "");

        Assert.Equal(3, count);
        Assert.Equal(120.0, eng);
    }

    [Fact]
    public void Estimate_CapsAtEightPeers()
    {
        var peers = BuildPeers(20, 100_000, "Commercial", "CD", "SmallJobs");

        var (_, _, count) = PeerBudgetEstimator.Estimate(100_000, "CD", "Commercial", "SmallJobs", peers, "");

        Assert.True(count <= 8);
    }

    // ── ComputeHeadline ─────────────────────────────────────────────────

    [Fact]
    public void ComputeHeadline_EmptyList()
    {
        var kpis = FinancialsHeadlineCalculator.Compute(new List<FinancialsProjectRow>());

        Assert.Equal(0, kpis.Projects);
        Assert.Equal(0.0, kpis.TotalFees);
        Assert.Equal(0.0, kpis.PercentHoursSpent);
        Assert.Equal(0.0, kpis.TeamDaysRemaining);
    }

    [Fact]
    public void ComputeHeadline_SingleProject_AllFieldsCorrect()
    {
        var rows = new List<FinancialsProjectRow>
        {
            new()
            {
                Fee = 200_000, FeeBilled = 100_000, Gfa = 5000,
                EngHrs = 80, DraftHrs = 40, EngBudget = 150, DraftBudget = 100,
            }
        };

        var kpis = FinancialsHeadlineCalculator.Compute(rows);

        Assert.Equal(1, kpis.Projects);
        Assert.Equal(200_000.0, kpis.TotalFees);
        Assert.Equal(100_000.0, kpis.TotalFeeBilled);
        Assert.Equal(100_000.0, kpis.TotalUnbilled);
        Assert.Equal(0.5, kpis.PercentFeeUnbilled, 6);
        Assert.Equal(5000.0, kpis.TotalGfa);
        Assert.Equal(40.0, kpis.AvgFeePerFt2, 6);  // 200k/5000
        Assert.Equal(120.0, kpis.HoursSpent);        // 80+40
        Assert.Equal(250.0, kpis.HoursBudgeted);     // 150+100
        Assert.Equal(130.0, kpis.HoursRemaining);    // 250-120
        Assert.Equal(120.0 / 250.0, kpis.PercentHoursSpent, 6);
    }

    [Fact]
    public void ComputeHeadline_AvgFeePerFt2_ExcludesZeroGfa()
    {
        var rows = new List<FinancialsProjectRow>
        {
            new() { Fee = 100_000, Gfa = 2000 },
            new() { Fee = 50_000, Gfa = 0 },  // no GFA — excluded from avg
        };

        var kpis = FinancialsHeadlineCalculator.Compute(rows);

        Assert.Equal(50.0, kpis.AvgFeePerFt2, 6);  // only 100k/2000, not (150k/2000)
    }

    [Fact]
    public void ComputeHeadline_MultiProject_Aggregation()
    {
        var rows = new List<FinancialsProjectRow>
        {
            new() { Fee = 100_000, FeeBilled = 80_000, EngHrs = 50, DraftHrs = 30, EngBudget = 100, DraftBudget = 60 },
            new() { Fee = 200_000, FeeBilled = 150_000, EngHrs = 100, DraftHrs = 50, EngBudget = 200, DraftBudget = 100 },
        };

        var kpis = FinancialsHeadlineCalculator.Compute(rows);

        Assert.Equal(2, kpis.Projects);
        Assert.Equal(300_000.0, kpis.TotalFees);
        Assert.Equal(230_000.0, kpis.TotalFeeBilled);
        Assert.Equal(70_000.0, kpis.TotalUnbilled);
        Assert.Equal(230.0, kpis.HoursSpent);      // (50+30) + (100+50)
        Assert.Equal(460.0, kpis.HoursBudgeted);    // (100+60) + (200+100)
    }

    [Fact]
    public void ComputeHeadline_TeamDaysRemaining_Formula()
    {
        var rows = new List<FinancialsProjectRow>
        {
            new() { EngHrs = 0, DraftHrs = 0, EngBudget = 750, DraftBudget = 0 },
        };

        var kpis = FinancialsHeadlineCalculator.Compute(rows);

        Assert.Equal(750.0 / AnalyticsThresholds.HoursPerDay / AnalyticsThresholds.TeamSize, kpis.TeamDaysRemaining, 6);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static List<PeerProject> BuildPeers(int count, double baseFee, string type, string phase, string category)
    {
        var list = new List<PeerProject>();
        for (int i = 0; i < count; i++)
        {
            list.Add(new PeerProject
            {
                Wbs1 = $"P{i:D3}",
                Fee = baseFee + (i - count / 2) * 1000,
                ConstructionType = type,
                Phase = phase,
                ProjectCategory = category,
                EngHrs = 100 + i * 10,
                DraftHrs = 50 + i * 5,
            });
        }
        return list;
    }
}
