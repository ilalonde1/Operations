#nullable enable
using AppFin = Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials.MetricDefinitions;

/// <summary>
/// Locks the Batch 72 contract: Definitions.Bd.cs provides 14 BD/CRM/
/// Opportunities concept entries (stages, win rate, pursuit duration,
/// client flags, lifetime fee, opportunity status, relevance score/tier,
/// discipline, buyer type, ingestion run). Each is referenced from one of
/// the three BD ViewModels' BuildContext methodology blocks. If any key
/// is renamed or removed, the methodology section silently shrinks — these
/// tests catch that drift.
/// </summary>
public sealed class BdMethodologyKeysTests
{
    // Kept in sync with what CrmViewModel.BuildContext (4),
    // ClientIntelligenceViewModel.BuildContext (5), and
    // OpportunitiesViewModel.BuildContext (6) actually request.
    public static readonly string[] BdAiKeys =
    {
        // CRM
        "Bd_EngagementStage",
        "Bd_WinRate",
        "Bd_PursuitDuration",
        // Client Intelligence
        "Bd_PriorWork",
        "Bd_RecommendFlag",
        "Bd_GovernmentAgency",
        "Bd_CompetitorFlag",
        "Bd_LifetimeFee",
        // Opportunities
        "Bd_OpportunityStatus",
        "Bd_RelevanceScore",
        "Bd_RelevanceTier",
        "Bd_OpportunityDiscipline",
        // Shared
        "Bd_BuyerType",
        "Bd_IngestionRun",
    };

    [Theory]
    [MemberData(nameof(KeyData))]
    public void EveryBdAiKey_ResolvesToMethodologyText(string key)
    {
        var methodology = AppFin.FinancialMetricDefinitions.TryGetAiMethodology(key);
        Assert.NotNull(methodology);
        Assert.NotEmpty(methodology);
    }

    [Fact]
    public void BuildAiMethodologyBlock_EmitsHeaderForEachBdKey()
    {
        var block = AppFin.FinancialMetricDefinitions.BuildAiMethodologyBlock(BdAiKeys);
        Assert.NotNull(block);
        foreach (var key in BdAiKeys)
        {
            Assert.Contains(key, block, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TryResolveKeyFromDisplayName_FindsBdEntryByDisplayName()
    {
        // Sanity: the reverse lookup also works for at least one BD entry
        // (the display name is what user-facing surfaces would feed in).
        var key = AppFin.FinancialMetricDefinitions.TryResolveKeyFromDisplayName("CRM Win Rate (trailing)");
        Assert.Equal("Bd_WinRate", key);
    }

    public static IEnumerable<object[]> KeyData() => BdAiKeys.Select(k => new object[] { k });
}
