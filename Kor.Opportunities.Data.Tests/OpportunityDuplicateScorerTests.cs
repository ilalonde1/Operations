using Kor.Opportunities.Data.Opportunities;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

public sealed class OpportunityDuplicateScorerTests
{
    // --- NameSimilarity: high for the same RFP phrased differently ---------

    [Theory]
    [InlineData("Consulting Engineering Services for the Coquitlam Landfill",
                "Consulting Engineering Services for the Coquitlam Landfill")]           // identical
    [InlineData("Seismic Upgrade Consulting Services",
                "Consulting Services for the Seismic Upgrade")]                            // reordered
    [InlineData("Karen Magnussen Community Recreation Centre",
                "ITT #26-04 Karen Magnussen Community Recreation Centre")]                 // procurement prefix
    public void NameSimilarity_SameRfp_ScoresHigh(string a, string b)
    {
        Assert.True(OpportunityDuplicateScorer.NameSimilarity(a, b) >= 0.60,
            $"expected high similarity for '{a}' vs '{b}'");
    }

    // --- NameSimilarity: low for genuinely different RFPs (even same buyer) -

    [Theory]
    [InlineData("Victoria General Hospital Parking Lot E",
                "Nanaimo Regional General Hospital Electrical Upgrade")]                   // both "general hospital"
    [InlineData("Site Selection and Functional Program Study",
                "Aquatic Diving Boards Replacement")]                                      // unrelated
    public void NameSimilarity_DifferentRfp_ScoresLow(string a, string b)
    {
        Assert.True(OpportunityDuplicateScorer.NameSimilarity(a, b) < 0.55,
            $"expected low similarity for '{a}' vs '{b}'");
    }

    // --- Classify: same buyer lowers the bar; no buyer needs near-identity --

    [Fact]
    public void Classify_SameBuyer_FlagsModerateScore()
    {
        // A moderate 0.5 name score is High for a shared buyer, None otherwise.
        Assert.Equal(DuplicateConfidence.Medium, OpportunityDuplicateScorer.Classify(0.50, sameBuyer: true));
        Assert.Equal(DuplicateConfidence.None, OpportunityDuplicateScorer.Classify(0.50, sameBuyer: false));
    }

    [Fact]
    public void Classify_DifferentBuyer_NeedsHighScore()
    {
        Assert.Equal(DuplicateConfidence.High, OpportunityDuplicateScorer.Classify(0.90, sameBuyer: false));
        Assert.Equal(DuplicateConfidence.None, OpportunityDuplicateScorer.Classify(0.30, sameBuyer: true));
    }

    // --- End-to-end: the real Bazaar dup pattern (two GVRD listings) --------

    [Fact]
    public void RealWorld_TwoPortalListingsSameRfp_FlaggedWhenSameBuyer()
    {
        // Abbreviation vs expansion (WWTP / Wastewater Treatment Plant) is a
        // genuine dup a human would catch; the guard must at least SURFACE it
        // (Medium or High) for the same buyer — not silently pass.
        var score = OpportunityDuplicateScorer.NameSimilarity(
            "Consulting Engineering Services for the Iona Island WWTP",
            "Consulting Engineering Services for Iona Island Wastewater Treatment Plant");
        Assert.NotEqual(DuplicateConfidence.None, OpportunityDuplicateScorer.Classify(score, sameBuyer: true));
    }

    [Fact]
    public void RealWorld_DifferentHospitalsSameHealthAuthority_NotFlagged()
    {
        var score = OpportunityDuplicateScorer.NameSimilarity(
            "Victoria General Hospital Parking Lot E",
            "Saanich Peninsula Hospital Parking Lot Rehabilitation");
        // Same buyer (Island Health) but genuinely different projects → not High.
        Assert.NotEqual(DuplicateConfidence.High, OpportunityDuplicateScorer.Classify(score, sameBuyer: true));
    }

    [Fact]
    public void NameSimilarity_BlankInputs_ScoreZero()
    {
        Assert.Equal(0.0, OpportunityDuplicateScorer.NameSimilarity(null, "x"));
        Assert.Equal(0.0, OpportunityDuplicateScorer.NameSimilarity("x", ""));
        Assert.Equal(0.0, OpportunityDuplicateScorer.NameSimilarity("  ", "  "));
    }
}
