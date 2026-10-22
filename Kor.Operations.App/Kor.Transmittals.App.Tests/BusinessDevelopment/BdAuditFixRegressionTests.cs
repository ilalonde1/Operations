#nullable enable
using System;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

// Screen-side fixes from the adversarial audit of 2026-10-21 (docs/codex/CODEX-BD-ADVERSARIAL-AUDIT-RESPONSE.md): the citation
// stripper behind the Director's question summary (C15) and the stage words of a tender past its closing date (C19).
// Not covered here: the question count on a card (needs a full board row) and the data-layer fixes (Data.Tests).
public sealed class BdAuditFixRegressionTests
{
    [Theory]
    [InlineData("Do we meet the minimum (3 comparable school projects)?", "Do we meet the minimum (3 comparable school projects)?")]
    [InlineData("Is the fee capped (see RFP 6.1)?", "Is the fee capped?")]
    [InlineData("Who signs (Schedule B 2.1; McCuaig FCA 3.1.1)?", "Who signs?")]
    [InlineData("Can KOR staff site visits (6.1.3)?", "Can KOR staff site visits?")]
    public void Only_real_references_are_stripped(string text, string expected)
        => Assert.Equal(expected, BdDirectorHomeViewModel.Uncited(text));

    [Fact]
    public void A_tender_past_its_closing_date_is_not_out_for_bid()
    {
        var past = new CrmEngagementRowView(new CrmEngagement { Stage = CrmEngagementStage.Drafting, OpportunityId = 1 },
            new Opportunity { SubmissionDeadlineUtc = DateTimeOffset.Now.AddDays(-1) });
        var open = new CrmEngagementRowView(new CrmEngagement { Stage = CrmEngagementStage.Drafting, OpportunityId = 1 },
            new Opportunity { SubmissionDeadlineUtc = DateTimeOffset.Now.AddDays(5) });
        Assert.Equal("Closed, not submitted", past.StageDisplay);
        Assert.Equal("Out for bid", open.StageDisplay);
    }
}
