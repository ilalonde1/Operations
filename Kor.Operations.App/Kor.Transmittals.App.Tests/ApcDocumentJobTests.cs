using Kor.Opportunities.Worker.Services;
using Xunit;

namespace Kor.Operations.App.Tests;

// Pins which pursuits the APC document job fetches. Compares SQL text only: it does not renew a session or download (that is
// checked live on Banff Centre AB-2026-06968 after the first sign-in), and a change in APC's download route would not be caught here.
public sealed class ApcDocumentJobTests
{
    [Fact]
    public void Only_live_pursuits_not_already_supplied_by_bids_and_tenders_are_fetched_once_then_daily_until_the_deadline()
    {
        var sql = ApcDocumentJob.TargetsSql;
        Assert.Contains("e.Stage IN (1, 3)", sql);                                 // pursued: downloading puts KOR on a public list
        Assert.Contains("x.Url LIKE N'https://purchasing.alberta.ca/posting/AB-%'", sql);
        Assert.Contains("b.Portal = N'BIDSTENDERS' AND b.Succeeded = 1 AND b.FilesInPackage > 0", sql); // same tender, package already in
        Assert.Contains("last.LastAt IS NULL", sql);
        Assert.Contains("o.SubmissionDeadlineUtc > SYSDATETIMEOFFSET() AND last.LastAt < DATEADD(hour, -20", sql);
        Assert.Contains("f.Portal = N'APC' AND f.Succeeded = 1", sql);
    }
}
