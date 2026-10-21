using Kor.Opportunities.Data.Opportunities;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Pins the tender merge rules the nightly OpportunityDedupJob and tools/BdOpportunityDedup share: the title core, the source
// of a key, and the gates. Pure functions only; the SQL merge itself is not exercised here, and a portal wording one tender
// two different ways beyond a reference number is still not found.
public sealed class OpportunityDedupTests
{
    private static OpportunityDedup.Row Row(long id, string key, string name, string buyer = "City", int status = 1,
        DateTime? deadline = null, bool pursuit = false, decimal? score = 10)
        => new(id, key, name, buyer, status, score, deadline.HasValue, false, pursuit, false,
            OpportunityDedup.CoreTitle(name), OpportunityDedup.SourceOf(key), deadline);

    [Theory]
    [InlineData("26-010 Building Condition Assessment and Feasibility Study – Building B", "building condition assessment and feasibility study building b")]
    [InlineData("Building Condition Assessment and Feasibility Study – Building B", "building condition assessment and feasibility study building b")]
    [InlineData("26-75-INF Kalamalka Lake Road Construction (Vernon (City))", "kalamalka lake road construction")]
    [InlineData("RFPQ# 26.0125 Bremner Water Supply Main Construction", "bremner water supply main construction")]
    public void A_reference_number_or_a_city_tag_does_not_make_a_different_tender(string title, string core)
        => Assert.Equal(core, OpportunityDedup.CoreTitle(title));

    [Fact]
    public void The_source_is_the_key_before_its_first_dash()
    {
        Assert.Equal("BCBID", OpportunityDedup.SourceOf("BCBID-229472"));
        Assert.Equal("BCBID", OpportunityDedup.SourceOf("BCBID-232044"));
        Assert.Equal("APCALLBU", OpportunityDedup.SourceOf("APCALLBU-AB-2026-06617"));
    }

    [Fact]
    public void Two_numbers_on_one_portal_are_held_unless_buyer_and_deadline_match()
    {
        // 2026-10-20: three different BC Bid notices sharing a generic title were nearly merged.
        var notices = new List<OpportunityDedup.Row>
        {
            Row(1, "BCBID-229472", "Notice to Vendors of upcoming procurement opportunities", "Ministry A"),
            Row(2, "BCBID-232044", "Notice to Vendors of upcoming procurement opportunities", "Ministry B"),
        };
        Assert.StartsWith("same portal", OpportunityDedup.GateGroup(notices, 0.90, out _));

        var day = new DateTime(2026, 11, 5);
        var repost = new List<OpportunityDedup.Row>
        {
            Row(3, "BCBID-234951", "Village of Valemount Municipal Building Entrance Sign RFP", deadline: day),
            Row(4, "BCBID-235006", "Village of Valemount Municipal Building Entrance Sign RFP", deadline: day),
        };
        Assert.Null(OpportunityDedup.GateGroup(repost, 0.90, out var survivor));
        Assert.NotNull(survivor);
    }

    [Fact]
    public void A_pursuit_or_a_status_disagreement_is_never_merged()
    {
        var withPursuit = new List<OpportunityDedup.Row>
        {
            Row(5, "APCALLBU-AB-2026-06617", "Request for Proposals for IPD Team Project Partner – Civil Consultant", pursuit: true),
            Row(6, "BIDSTEND-RFP-IP-065", "Request for Proposals for IPD Team Project Partner – Civil Consultant"),
        };
        Assert.Contains("pursuit", OpportunityDedup.GateGroup(withPursuit, 0.90, out _));

        var liveAndDead = new List<OpportunityDedup.Row>
        {
            Row(7, "APCALLBU-AB-2026-1", "Hythe Recreational Facility Waterworks Improvements", status: 1),
            Row(8, "BIDSTEND-2026-9", "Hythe Recreational Facility Waterworks Improvements", status: 7),
        };
        Assert.Contains("Status", OpportunityDedup.GateGroup(liveAndDead, 0.90, out _));
    }

    [Fact]
    public void Cross_posted_copies_merge_into_the_best_scored_one()
    {
        var rows = new List<OpportunityDedup.Row>
        {
            Row(9, "BCBID-234347", "Building Condition Assessment and Feasibility Study – Building B", score: 13),
            Row(10, "BONFIREU-26-010", "26-010 Building Condition Assessment and Feasibility Study – Building B", score: 5),
            Row(11, "BCBID-111111", "Something else entirely about a library roof"),
        };
        var outcome = OpportunityDedup.BuildPlan(rows, includeSameSource: true, minSimilarity: 0.90);
        var plan = Assert.Single(outcome.Plans);
        Assert.Equal(9, plan.Survivor.Id);
        Assert.Equal(10, plan.Loser.Id);
    }
}
