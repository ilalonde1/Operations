using System;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

// What a newly assigned owner is told (2026-10-21), and who counts as "Mine". Checks the email body and the match rule only:
// it does not send through Graph, write the My day ask or reassign in the database; a Graph or ledger failure path is not
// exercised here.
public sealed class PursuitAssignmentNoticeTests
{
    private static readonly PursuitReviewRow Review = new(1, 1142, 7, "decide", "An MSA.", "Civil is a minimum qualification.\nPricing is 40 points.",
        new[] { new RfpQuestion("Can a civil sub-consultant be named?", "KOR is structural only.", "Appendix E A.3") },
        "Fri 23 Oct 2026, 2:00 pm MST", "a@b.ca", null, null, "Claude", DateTimeOffset.UtcNow,
        new[]
        {
            new RfpAction("Decide go or no-go", "Ian", new DateOnly(2026, 10, 22), "Questions close 23 Oct."),
            new RfpAction("Send the questions", "owner", new DateOnly(2026, 10, 22), "Last day for questions is 23 Oct."),
        });

    [Fact]
    public void The_email_tells_the_new_owner_the_verdict_and_only_their_own_actions()
    {
        var a = new PursuitAssignment(1142, null, "Banff Centre PSA", "Banff Centre", null, "ishabana@korstructural.com", "Islam",
            "ilalonde@korstructural.com", "Your region.");
        var body = PursuitAssignmentNotice.Body(a, "Islam", Review);
        Assert.Contains("is now yours", body);
        Assert.Contains("Your region.", body);
        Assert.Contains("Claude read the RFP: Your call.", body);
        Assert.Contains("Send the questions", body);
        Assert.DoesNotContain("Decide go or no-go", body); // Ian's action, not Islam's
        Assert.Contains("1 question for the buyer", body);

        var plain = PursuitAssignmentNotice.Body(a, "Islam", null);
        Assert.DoesNotContain("Claude read the RFP", plain);
    }

    [Fact]
    public void Mine_is_the_owner_or_a_second_seat_and_nothing_looser()
    {
        var e = new CrmEngagement { Id = 1, OwnerStaffId = "ishabana@korstructural.com", AssignedStaffIds = "rwong@korstructural.com, jbryson@korstructural.com" };
        Assert.True(CrmViewModel.IsMine(e, "ISHABANA@korstructural.com"));
        Assert.True(CrmViewModel.IsMine(e, "jbryson@korstructural.com"));
        Assert.False(CrmViewModel.IsMine(e, "rwong"));
        Assert.False(CrmViewModel.IsMine(e, null));
    }
}
