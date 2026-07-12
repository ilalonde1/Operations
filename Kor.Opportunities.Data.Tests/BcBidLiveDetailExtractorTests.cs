using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Pins the pure DOM-text -> fields parser used by the Phase-2 live-opp detail
// enricher. Fixture mirrors a real BC Bid (Ivalua) RFP detail page — the RIH
// ERCP opportunity (process 230528) that motivated the enrichment work.
public sealed class BcBidLiveDetailExtractorTests
{
    private const string RihPageText = @"RFx General Information
Opportunity Description
RFP RIH ERCP Service Delivery - Architectural and Engineering Services
Issued by Interior Health Authority
Other Commodities
72120000 - Nonresidential building construction services
72121403 - Hospital construction service
81101505 - Structural engineering
81101508 - Architectural engineering
81101600 - Mechanical engineering
81101701 - Electrical engineering services
Official Contact Information
Contact First Name Contact Last Name Email
Sarah DeWolfe IHCPPProcurementTeam@interiorhealth.ca
Enquiries related to this RFx. Phone 250-555-1234.
RFx Documents for RFP RIH ERCP Service Delivery";

    private static readonly DetailLink[] RihLinks =
    {
        new("Home", "https://bcbid.gov.bc.ca/page.aspx/en/buy/homepage"),
        new("RIH ERCP - Terms of Reference.pdf", "https://bcbid.gov.bc.ca/download.aspx?blobId=98765"),
        new("Contact enquiryBC", "mailto:bcbid@gov.bc.ca"),
    };

    [Fact]
    public void ParsesCommoditiesContactAndDocuments()
    {
        var r = BcBidLiveDetailExtractor.ParseDetail(RihPageText, RihLinks);

        Assert.Contains(r.CommodityCodes, c => c.Contains("81101505"));
        Assert.Contains(r.CommodityCodes, c => c.Contains("81101508"));
        // The commodity list drives the KOR discipline: structural + other = Mixed.
        Assert.Equal(OpportunityDiscipline.Mixed,
            DisciplineClassifier.Classify(r.CommodityCodes, "RFP RIH ERCP", null));

        // The issuing-authority contact, NOT a BC Bid system address.
        Assert.Equal("IHCPPProcurementTeam@interiorhealth.ca", r.ContactEmail);
        Assert.Equal("Sarah DeWolfe", r.ContactName);
        Assert.Equal("250-555-1234", r.ContactPhone);

        // The RFP PDF is captured; nav/help links are not.
        Assert.Single(r.Documents);
        Assert.Contains(r.Documents, d => d.Url.Contains("blobId=98765"));
    }

    [Fact]
    public void EmptyPage_YieldsEmptyResult_NotNull()
    {
        var r = BcBidLiveDetailExtractor.ParseDetail("", System.Array.Empty<DetailLink>());
        Assert.Empty(r.CommodityCodes);
        Assert.Empty(r.Documents);
        Assert.Null(r.ContactEmail);
    }

    [Fact]
    public void SystemEmailsAreRejected_NoJunkContact()
    {
        // System/vendor addresses (bcbid, bidsandtenders, support@, gov.bc.ca) must
        // NOT be persisted as the buyer contact — null is better than misleading.
        var r = BcBidLiveDetailExtractor.ParseDetail(
            "Submit via support@bidsandtenders.ca. Questions to bcbid@gov.bc.ca.",
            System.Array.Empty<DetailLink>());
        Assert.Null(r.ContactEmail);
    }
}
