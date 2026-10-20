using Kor.Opportunities.Worker.Services;
using Xunit;

namespace Kor.Operations.App.Tests;

// Pins how the Bids&Tenders document job finds a package and which pursuits it fetches. Compares URL mapping and SQL text
// only: it does not sign in or download (that was checked live on Leduc 2026-RFP-IP-061 before the deploy: one 45 MB zip of
// nine files), and a change in the site's download path would not be caught here.
public sealed class BidsAndTendersDocumentJobTests
{
    [Fact]
    public void A_tender_page_maps_to_its_package_download_and_nothing_else_does()
    {
        Assert.Equal("https://leduc.bidsandtenders.ca/Module/Tenders/en/Tender/DownloadBidDocuments/5874ae1a-74e2-43c0-8e92-5554aca54653",
            BidsAndTendersDocumentJob.DownloadUrlFor("https://leduc.bidsandtenders.ca/Module/Tenders/en/Tender/Detail/5874ae1a-74e2-43c0-8e92-5554aca54653"));
        Assert.Equal("https://burnaby.bidsandtenders.ca/Module/Tenders/fr/Tender/DownloadBidDocuments/9fc4128a-b8cb-4d14-a41d-8c3de8f2fa1d",
            BidsAndTendersDocumentJob.DownloadUrlFor("https://burnaby.bidsandtenders.ca/Module/Tenders/fr/Tender/Detail/9fc4128a-b8cb-4d14-a41d-8c3de8f2fa1d/#Document"));
        Assert.Null(BidsAndTendersDocumentJob.DownloadUrlFor("https://purchasing.alberta.ca/posting/AB-2026-06968"));
        Assert.Null(BidsAndTendersDocumentJob.DownloadUrlFor("https://leduc.bidsandtenders.ca/Module/Tenders/en/Tender/Detail/not-a-guid"));
        Assert.Null(BidsAndTendersDocumentJob.DownloadUrlFor("https://evil.example/Module/Tenders/en/Tender/Detail/5874ae1a-74e2-43c0-8e92-5554aca54653"));
    }

    [Fact]
    public void Only_live_pursuits_are_fetched_once_then_daily_until_the_deadline()
    {
        var sql = BidsAndTendersDocumentJob.TargetsSql;
        Assert.Contains("e.Stage IN (1, 3)", sql);                               // pursued: never an unclaimed tender (plan-taker list)
        Assert.Contains("last.LastAt IS NULL", sql);                             // never fetched
        Assert.Contains("o.SubmissionDeadlineUtc > SYSDATETIMEOFFSET() AND last.LastAt < DATEADD(hour, -20", sql); // addenda while open
        Assert.Contains("f.Succeeded = 1", sql);                                 // a failed fetch is retried next hour
    }
}
