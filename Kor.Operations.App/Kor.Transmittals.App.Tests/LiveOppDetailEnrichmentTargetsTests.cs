using Kor.Opportunities.Worker.Services;
using Xunit;

namespace Kor.Operations.App.Tests;

// Pins the three queue rules of the live detail job (2026-10-20: the APC reader's first run read 1 of 37 live postings and
// skipped the grabbed Banff Centre RFP). Compares the SQL text only, so it cannot tell that the rules give the right ORDER on
// real rows; that was checked by running TargetsSql read-only against the live database before the deploy. A rule written
// differently but equivalently would fail here and need this test updated.
public sealed class LiveOppDetailEnrichmentTargetsTests
{
    private static string Sql => LiveOppDetailEnrichmentJob.TargetsSql;

    [Fact]
    public void Grabbed_opportunities_are_read_and_come_first()
    {
        Assert.Contains("o.Status IN (0, 1, 4)", Sql);
        Assert.Contains("ORDER BY CASE WHEN o.Status = 4 OR EXISTS (SELECT 1 FROM opportunities.CrmEngagements e WHERE e.OpportunityId = o.Id) THEN 0 ELSE 1 END", Sql);
    }

    [Fact]
    public void Closed_and_dismissed_postings_are_not_read()
    {
        Assert.Contains("(o.SubmissionDeadlineUtc IS NULL OR o.SubmissionDeadlineUtc > SYSDATETIMEOFFSET())", Sql);
        Assert.Contains("o.DismissedAtUtc IS NULL", Sql);
        Assert.Contains("o.DetailEnrichedAtUtc IS NULL", Sql);
    }
}
