#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.MetricDefinitions;

/// <summary>
/// Locks the Batch 71 contract: AnalyticsAiService.BuildContext emits the
/// methodology block for eight Hist_* dictionary keys. If a key is renamed
/// or removed from Definitions.Historical.cs the methodology section
/// silently shrinks without any compile-time signal — these tests catch
/// that drift.
/// </summary>
public sealed class HistoricalMethodologyKeysTests
{
    public static readonly string[] HistAiKeys =
    {
        "Hist_PctBilled",
        "Hist_FeePerHr",
        "Hist_EngPct",
        "Hist_DraftPct",
        "Hist_BillablePct",
        "Hist_SubPctOfFee",
        "Hist_EngDelta",
        "Hist_DraftDelta",
    };

    [Theory]
    [MemberData(nameof(KeyData))]
    public void EveryHistAiKey_ResolvesToMethodologyText(string key)
    {
        var methodology = AppFin.FinancialMetricDefinitions.TryGetAiMethodology(key);
        Assert.NotNull(methodology);
        Assert.NotEmpty(methodology);
    }

    [Fact]
    public void BuildAiMethodologyBlock_EmitsHeaderForEachHistKey()
    {
        var block = AppFin.FinancialMetricDefinitions.BuildAiMethodologyBlock(HistAiKeys);
        Assert.NotNull(block);
        foreach (var key in HistAiKeys)
        {
            Assert.Contains(key, block, System.StringComparison.Ordinal);
        }
    }

    public static IEnumerable<object[]> KeyData() => HistAiKeys.Select(k => new object[] { k });
}
