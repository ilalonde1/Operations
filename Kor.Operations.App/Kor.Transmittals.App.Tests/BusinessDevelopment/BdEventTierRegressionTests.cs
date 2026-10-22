#nullable enable
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

// The Events tier read from the researched verdict at the head of "Why". Covers the verdict token only; the keyword score
// used when there is no verdict is not tested here.
public sealed class BdEventTierRegressionTests
{
    [Theory]
    [InlineData("HIGH - Alberta's major building industry tradeshow", "Tier 1")]
    [InlineData("Highest GC/owner density of any western-Canada series", "Tier 1")]
    [InlineData("MEDIUM - Annual Washington state architecture awards", "Tier 2")]
    [InlineData("LOW — Primary LA-market architecture awards program", "Watch")]
    // Adversarial audit M7, 2026-10-21: prose that only starts with the letters is not a verdict.
    [InlineData("Lower Mainland school board buyers attend", null)]
    [InlineData("Highway suppliers only; no building buyers", null)]
    [InlineData("The procurement process for defence work", null)]
    [InlineData(null, null)]
    public void The_tier_comes_from_a_real_verdict_only(string? why, string? tier)
        => Assert.Equal(tier, EventsViewModel.ResearchedTier(why));
}
