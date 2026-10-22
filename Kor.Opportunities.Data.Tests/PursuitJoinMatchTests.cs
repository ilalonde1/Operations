using Kor.Opportunities.Data.Crm;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Which forward pursuit a grabbed tender joins (SqlPursuitGrabStore.Best). Covers the name score, the province and the
// municipality check; not the database read that feeds them, and not two candidates scoring the same (first one wins).
public sealed class PursuitJoinMatchTests
{
    private static ForwardMatch Pursuit(string project, string? municipality, string province = "BC")
        => new(1, project, "owner@korstructural.com", "Owner", 0) { Province = province, Municipality = municipality };

    // Adversarial audit C9, 2026-10-21: three generic words scored exactly 0.60 and moved Surrey's tender to Burnaby's owner.
    [Fact]
    public void A_tender_in_another_town_does_not_join_on_generic_words()
        => Assert.Null(SqlPursuitGrabStore.Best("Surrey Fire Hall Replacement - Prime Consultant Services", "BC",
            new[] { Pursuit("Burnaby Fire Hall Replacement", "Burnaby") }, "Surrey Fire Hall Replacement - Prime Consultant Services City of Surrey Surrey"));

    [Fact]
    public void A_tender_that_names_the_projects_town_joins()
        => Assert.NotNull(SqlPursuitGrabStore.Best("Mayerthorpe School Replacement - Prime Consultant Services - RFP", "AB",
            new[] { Pursuit("Mayerthorpe School Replacement", "Town of Mayerthorpe", "AB") }, "Mayerthorpe School Replacement - Prime Consultant Services - RFP Lac Ste. Anne Foundation"));

    [Fact]
    public void With_no_municipality_on_file_the_names_must_nearly_match()
    {
        var tender = "Fire Hall Replacement and Training Tower - Prime Consultant";
        Assert.Null(SqlPursuitGrabStore.Best(tender, "BC", new[] { Pursuit("Burnaby Fire Hall Replacement", null) }, tender));
        Assert.NotNull(SqlPursuitGrabStore.Best("RFP Testburg Library Expansion Prime Consultant", "BC",
            new[] { Pursuit("Testburg Library Expansion", null) }, null));
    }

    [Fact]
    public void A_tender_in_another_province_never_joins()
        => Assert.Null(SqlPursuitGrabStore.Best("Mayerthorpe School Replacement", "BC",
            new[] { Pursuit("Mayerthorpe School Replacement", "Mayerthorpe", "AB") }, "Mayerthorpe School Replacement"));
}
