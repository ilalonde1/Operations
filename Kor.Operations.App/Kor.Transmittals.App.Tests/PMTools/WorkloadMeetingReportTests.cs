#nullable enable
using System;
using System.Collections.Generic;
using Kor.Operations.App.PMTools;
using Xunit;

namespace Kor.Operations.App.Tests.PMTools;

/// <summary>
/// The end-of-meeting report (spec R17, Jim 13 Mar 1:04:10): which jobs it lists, in which
/// section, and who it is addressed to.
///
/// Covers: due / no-deadline / by-PM sections, P0 rows left out, the window edge, notes carried,
/// recipients as PM + drafting manager emails with unknown names returned apart.
/// Does NOT cover: the Deltek EMMain read, the Outlook draft itself (OutlookDraft only displays,
/// never sends), or the window wiring. A same-class fault it would miss: a name formatted
/// differently in EMMain than in the project rows, because the email map here is hand-built.
/// </summary>
public sealed class WorkloadMeetingReportTests
{
    private static readonly DateTime Today = new(2026, 11, 26);
    private static readonly DateTime Meeting = new(2026, 11, 30);

    private static WorkloadMeetingReport.Job Job(string wbs1, int p, string pm, string dm, DateTime? due, string notes = "")
        => new(wbs1, "Name " + wbs1, p, pm, dm, due, due is null ? "" : "Progress - 60%", notes);

    private static readonly List<WorkloadMeetingReport.Job> Jobs = new()
    {
        Job("31251-01", 1, "Conor Murtagh", "Serban Acosti", Today.AddDays(1), "Mark designing concrete"),
        Job("31184-01", 2, "Jason Stuart", "", Today.AddDays(5)),
        Job("31206-01", 1, "Rory Beirne", "Serban Acosti", Today.AddDays(19)),   // beyond 2 weeks
        Job("90110-01", 1, "John Bryson", "", null, "JB only this week"),
        Job("30000-01", 0, "Omar Alcazar", "Lee", Today.AddDays(2)),             // not prioritised
    };

    [Fact]
    public void Html_ListsDueInWindow_NoDeadlineApart_AndEveryPrioritisedJobByPm()
    {
        var html = WorkloadMeetingReport.Html(Meeting, Today, 2, Jobs, "Next meeting: 14 Dec & <later>", new[] { "John Bryson" });

        Assert.Contains("4 prioritised jobs &middot; 2 due in the next 2 weeks &middot; 1 with no deadline in Deltek", html);
        var dueStart = html.IndexOf("Due in the next 2 weeks</p>", StringComparison.Ordinal);
        var noneStart = html.IndexOf("No deadline in Deltek (1)", StringComparison.Ordinal);
        Assert.True(dueStart > 0 && noneStart > dueStart);
        var dueSection = html[dueStart..noneStart];
        Assert.True(dueSection.IndexOf("31251-01", StringComparison.Ordinal) < dueSection.IndexOf("31184-01", StringComparison.Ordinal),
            "due list must be in date order");
        Assert.DoesNotContain("31206-01", dueSection);          // beyond the window: not in the due table
        Assert.Contains("JB only this week", html);
        Assert.Contains("Rory Beirne <span", html);             // still listed under its PM
        Assert.DoesNotContain("30000-01", html);                 // P0 never in the report
        Assert.Contains("Not addressed (no email in Deltek): John Bryson", html);
        Assert.Contains("Next meeting: 14 Dec &amp; &lt;later&gt;", html);   // notes are HTML-encoded
    }

    [Fact]
    public void Recipients_ArePmsAndDraftingManagers_WithUnknownNamesApart()
    {
        var emails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Conor Murtagh"] = "conor@example.test",
            ["Serban Acosti"] = "serban@example.test",
            ["Jason Stuart"] = "jason@example.test",
            ["Omar Alcazar"] = "omar@example.test",
            ["Lee"] = "lee@example.test",
        };

        var (to, unmatched) = WorkloadMeetingReport.Recipients(Jobs, emails);

        Assert.Equal(new[] { "conor@example.test", "jason@example.test", "serban@example.test" }, to);
        Assert.Equal(new[] { "John Bryson", "Rory Beirne" }, unmatched);   // prioritised, no email on file
    }
}
