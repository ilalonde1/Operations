using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Pins the B&T detail parser. Fixture mirrors a real Bids&Tenders (Metro
// Vancouver) tender page — where the listing scrape stores NO description, so the
// detail page's Description is the value (it feeds the discipline classifier).
public sealed class BidsAndTendersLiveDetailExtractorTests
{
    private const string RealPageText = @"Bid Details
Bid Classification: 	Services
Bid Type: 	RFP
Bid Number: 	25-359
Bid Name: 	Consulting Engineering Services for Parks Projects
Bid Status: 	Open
Description:

The Work includes the provision of professional engineering, project management, and related consulting services required by the Corporation in support of its Parks infrastructure programs and capital projects.

Bid Document Access: 	Bid Opportunity notices and awards ...";

    [Fact]
    public void ExtractsDescriptionFromRealPage()
    {
        var r = BidsAndTendersLiveDetailExtractor.ParseDetail(RealPageText);
        Assert.NotNull(r.Description);
        Assert.Contains("professional engineering", r.Description!);
        Assert.DoesNotContain("Bid Document Access", r.Description!);
        // General consulting engineering, not specifically structural -> Unknown (correct).
        Assert.Equal(OpportunityDiscipline.Unknown,
            DisciplineClassifier.Classify(r.CommodityCodes, "Consulting Engineering Services", r.Description));
    }

    [Fact]
    public void StructuralTender_DescriptionDrivesDiscipline()
    {
        const string text = @"Bid Name: 	Bridge Rehabilitation
Description: 	Structural engineering services for the seismic retrofit of the Main Street bridge, including analysis and design.
Bid Document Access: 	free preview";
        var r = BidsAndTendersLiveDetailExtractor.ParseDetail(text);
        Assert.Contains("seismic retrofit", r.Description!);
        Assert.Equal(OpportunityDiscipline.Structural,
            DisciplineClassifier.Classify(r.CommodityCodes, "Bridge Rehabilitation", r.Description));
    }

    [Fact]
    public void NoDescription_SafeEmptyResult()
    {
        var r = BidsAndTendersLiveDetailExtractor.ParseDetail("Some page with no description section.");
        Assert.Null(r.Description);
        Assert.Empty(r.Documents);
        Assert.Empty(r.CommodityCodes);
    }
}
