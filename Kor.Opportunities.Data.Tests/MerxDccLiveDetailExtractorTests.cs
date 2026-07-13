using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Pins the MERX/DCC detail parser against the real page text (CX24SL03 — a DCC
// Pacific solicitation at CFB Comox). The value is the description (→ discipline)
// and the real issuing contact.
public sealed class MerxDccLiveDetailExtractorTests
{
    private const string RealPageText = @"CX24SL03_CN83345 - Mechanical Construction Source List for Quick Response Tenders (QRT)
Select a tab
Notice
Categories
Documents (10)
Plan Holders List (37)
Basic Information
Reference Number

0000308389

Issuing Organization

Defence Construction Canada - Pacific Region

Description

DEFENCE CONSTRUCTION CANADA (DCC) - Mechanical Construction Source List for Quick Response Tenders (QRT) – 19 Wing, CFB Comox, Lazo, BC, CX24SL03

...
See more

Dates
Publication

2025/11/26 04:38:56 PM PST

Contact Information

Michael Simons

236-255-1230

Comox.Contracting@dcc-cdc.gc.ca

Bid Submission Process";

    [Fact]
    public void ExtractsDescriptionAndRealContact()
    {
        var r = MerxDccLiveDetailExtractor.ParseDetail(RealPageText);

        Assert.NotNull(r.Description);
        Assert.Contains("DEFENCE CONSTRUCTION CANADA", r.Description!);
        Assert.DoesNotContain("See more", r.Description!);

        Assert.Equal("Michael Simons", r.ContactName);
        Assert.Equal("236-255-1230", r.ContactPhone);
        Assert.Equal("Comox.Contracting@dcc-cdc.gc.ca", r.ContactEmail);
    }

    [Fact]
    public void StructuralSolicitation_DescriptionDrivesDiscipline()
    {
        const string text = @"Description

Structural engineering services for seismic upgrade of the drill hall, CFB Esquimalt.

...
See more
Dates";
        var r = MerxDccLiveDetailExtractor.ParseDetail(text);
        Assert.Equal(OpportunityDiscipline.Structural,
            DisciplineClassifier.Classify(r.CommodityCodes, "Drill Hall Seismic", r.Description));
    }

    [Fact]
    public void NoContactSection_SafeResult()
    {
        var r = MerxDccLiveDetailExtractor.ParseDetail("Some MERX page with no contact block.");
        Assert.Null(r.ContactEmail);
        Assert.Null(r.ContactName);
    }
}
