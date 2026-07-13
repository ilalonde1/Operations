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

    // ---- Tab-fragment parsers (v2, DCC subscription) -------------------------
    // MERX's tab endpoints return page chrome plus an inline script
    // $("#innerTabContent").html('<escaped markup>') — the content exists ONLY
    // as that JS string literal (three live iterations proved neither URL
    // navigation nor tab clicks render it). These pin the decode + extraction,
    // escape shapes copied from the real authenticated fragment (0000324212).

    private const string RequestListFragment =
        "<html><body><script>var x=1;</script>" +
        "$(\"#innerTabContent\").html('\\u003Cdiv class=\\\"content-block\\\"\\u003E\\n" +
        "\\u003Ctable id=\\\"documentRequesTable\\\" class=\\\"contentBlockTable basic mets-table\\\"\\u003E\\n" +
        "\\u003Cthead\\u003E\\u003Ctr\\u003E\\u003Cth scope=\\\"col\\\"\\u003EOrganization\\u003C\\/th\\u003E\\u003C\\/tr\\u003E\\u003C\\/thead\\u003E" +
        "\\u003Ctbody\\u003E" +
        "\\u003Ctr\\u003E\\u003Ctd\\u003E Read Jones Christoffersen Ltd. \\u003C\\/td\\u003E\\u003Ctd\\u003EVictoria\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003Ctr\\u003E\\u003Ctd\\u003EStantec Architecture\\u0026nbsp;Ltd.\\u003C\\/td\\u003E\\u003Ctd\\u003EVancouver\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003Ctr\\u003E\\u003Ctd\\u003ERead Jones Christoffersen Ltd.\\u003C\\/td\\u003E\\u003Ctd\\u003EDup row\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003C\\/tbody\\u003E\\u003C\\/table\\u003E\\u003C\\/div\\u003E');" +
        "</body></html>";

    private const string DocsFragment =
        "<html><body>" +
        "$(\"#innerTabContent\").html('\\u003Cdiv\\u003E" +
        "\\u003Ctable id=\\\"preview_tblSolDocuments0\\\"\\u003E" +
        "\\u003Ctr\\u003E\\u003Ctd\\u003E\\u003Ca href=\\\"\\/private\\/solicitations\\/4001461209\\/abstract\\/view-document?id=1\\\"\\u003EAcknowledgement_document_MDB-R2026-01.pdf\\u003C\\/a\\u003E\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003Ctr\\u003E\\u003Ctd\\u003E\\u003Ca href=\\\"\\/private\\/solicitations\\/4001461209\\/abstract\\/view-document?id=1\\\"\\u003Eduplicate link\\u003C\\/a\\u003E\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003Ctr\\u003E\\u003Ctd\\u003E\\u003Ca href=\\\"\\/public\\/solicitations\\/open\\\"\\u003Enot a doc\\u003C\\/a\\u003E\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003C\\/table\\u003E\\u003C\\/div\\u003E');" +
        "</body></html>";

    [Fact]
    public void DecodesTheInnerTabLiteral()
    {
        var html = MerxDccLiveDetailExtractor.DecodeInnerTabHtml(RequestListFragment);
        Assert.NotNull(html);
        Assert.Contains("<table id=\"documentRequesTable\"", html);
        Assert.Contains("</table>", html);
    }

    [Fact]
    public void PlanHolders_ComeOnlyFromTheRequestTable_Deduped()
    {
        var firms = MerxDccLiveDetailExtractor.ParsePlanHoldersFragment(RequestListFragment);
        Assert.Equal(2, firms.Count);
        Assert.Contains("Read Jones Christoffersen Ltd.", firms);
        // &nbsp; (&nbsp;) decodes to a NBSP then collapses to a space.
        Assert.Contains(firms, f => f.StartsWith("Stantec Architecture", StringComparison.Ordinal));
    }

    [Fact]
    public void Documents_OnlyRealFileLinks_AbsoluteUrls_Deduped()
    {
        var docs = MerxDccLiveDetailExtractor.ParseDocumentsFragment(DocsFragment);
        var doc = Assert.Single(docs);
        Assert.Equal("Acknowledgement_document_MDB-R2026-01.pdf", doc.Name);
        Assert.StartsWith("https://www.merx.com/private/solicitations/", doc.Url);
    }

    [Fact]
    public void FragmentWithoutInnerTabScript_YieldsNothing()
    {
        Assert.Empty(MerxDccLiveDetailExtractor.ParsePlanHoldersFragment("<html>login wall</html>"));
        Assert.Empty(MerxDccLiveDetailExtractor.ParseDocumentsFragment("<html>login wall</html>"));
    }
}
