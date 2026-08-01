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

    // Escape shapes + the OrganizationName span and expandable detail row are
    // copied from the real authenticated fragment (0000324212, 2026-07-13):
    // the org name lives in <span class="mets-tree-table-node-OrganizationName">
    // and each firm has a data-child-of detail row (Address/Contact/Phone) that
    // must NOT be scraped as a firm.
    private const string RequestListFragment =
        "<html><body><script>var x=1;</script>" +
        "$(\"#innerTabContent\").html('\\u003Cdiv class=\\\"content-block\\\"\\u003E\\n" +
        "\\u003Ctable id=\\\"documentRequesTable\\\"\\u003E\\n" +
        "\\u003Ctr\\u003E\\u003Ctd\\u003E\\u003Cspan class=\\\"mets-tree-table-node-OrganizationName\\\"\\u003EJCK Engineering\\u003C\\/span\\u003E\\u003C\\/td\\u003E\\u003Ctd\\u003ERegina\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003Ctr id=\\\"expansion_1\\\" data-child-of=\\\"1\\\"\\u003E\\u003Ctd\\u003EAddress 2424 College Avenue Phone 306-585-6126 Email x\\u0040y.com\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003Ctr\\u003E\\u003Ctd\\u003E\\u003Cspan class=\\\"mets-tree-table-node-OrganizationName\\\"\\u003EGHD Limited\\u003C\\/span\\u003E\\u003C\\/td\\u003E\\u003Ctd\\u003EWaterloo\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003Ctr\\u003E\\u003Ctd\\u003E\\u003Cspan class=\\\"mets-tree-table-node-OrganizationName\\\"\\u003EJCK Engineering\\u003C\\/span\\u003E\\u003C\\/td\\u003E\\u003Ctd\\u003EDup\\u003C\\/td\\u003E\\u003C\\/tr\\u003E" +
        "\\u003C\\/table\\u003E\\u003C\\/div\\u003E');" +
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
    public void PlanHolders_ComeOnlyFromOrgNameSpans_ExpansionRowsIgnored_Deduped()
    {
        var firms = MerxDccLiveDetailExtractor.ParsePlanHoldersFragment(RequestListFragment);
        Assert.Equal(new[] { "JCK Engineering", "GHD Limited" }, firms);
        // The data-child-of detail row (Address/Phone/Email) is not a firm.
        Assert.DoesNotContain(firms, f => f.Contains("Address", StringComparison.OrdinalIgnoreCase));
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
