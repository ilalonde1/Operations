#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.MetricDefinitions;

/// <summary>
/// Locks the Batch 70 contract: PmToolsViewModel.BuildContext emits the
/// "KPI methodology" block for ten PmTools_* dictionary keys. If any
/// key is renamed or removed from Definitions.PmTools.cs, the methodology
/// emission goes silent for that KPI without any compiler error. These
/// tests catch the drift.
/// </summary>
public sealed class PmToolsMethodologyKeysTests
{
    public static readonly string[] PmToolsAiKeys =
    {
        "PmTools_ActiveProjects",
        "PmTools_AtRiskCritical",
        "PmTools_DeliveryRisk",
        "PmTools_CapacityRisk",
        "PmTools_FeeRemaining",
        "PmTools_PercentBilled",
        "PmTools_FeePerHours",
        "PmTools_BilledPerHours",
        "PmTools_EngPercent",
        "PmTools_DraftPercent",
    };

    [Theory]
    [MemberData(nameof(KeyData))]
    public void EveryPmToolsAiKey_ResolvesToMethodologyText(string key)
    {
        var methodology = AppFin.FinancialMetricDefinitions.TryGetAiMethodology(key);
        Assert.NotNull(methodology);
        Assert.NotEmpty(methodology);
    }

    [Fact]
    public void BuildAiMethodologyBlock_EmitsHeaderForEachPmToolsKey()
    {
        // The bulk helper that PmToolsViewModel.BuildContext actually calls.
        // It should emit DisplayName headers for every key we list — if a
        // key is unknown it's silently dropped, which is what we want to
        // protect against here.
        var block = AppFin.FinancialMetricDefinitions.BuildAiMethodologyBlock(PmToolsAiKeys);
        Assert.NotNull(block);

        // Each key should appear at least once in the output (as part of the
        // "DisplayName (key)" header line). One failure = silent KPI drop.
        foreach (var key in PmToolsAiKeys)
        {
            Assert.Contains(key, block, System.StringComparison.Ordinal);
        }
    }

    public static IEnumerable<object[]> KeyData() => PmToolsAiKeys.Select(k => new object[] { k });
}
