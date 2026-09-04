#nullable enable
using System.Linq;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// WHAT THESE COVER: the fields that make this page worth reading at all — the
/// APPLICATION CONTACT (the applicant's own agent, which exists in no feed we
/// ingest), including the JS-obfuscated email; the purpose and address list; and
/// the submitted plan sets. Fragments below are copied verbatim from the live
/// page for REZ00901 (846 Broughton St, three mixed-use towers), captured
/// 2026-09-03.
///
/// WHAT THEY DO NOT COVER: navigation, login, or the Playwright wrapper — only
/// the pure ParseDetail. A same-class fault they would NOT catch: Victoria
/// renaming its ASP.NET control ids, which would make every field go null while
/// the page still returns HTTP 200. That shows up as the enrichment job filling
/// nothing, not as a parse error.
/// </summary>
public sealed class VictoriaProsperoLiveDetailExtractorTests
{
    private const string DetailUrl =
        "https://tender.victoria.ca/webapps/ourcity/prospero/details.aspx?folderNumber=REZ00901";

    // The applicant block, verbatim. The email is a char-pair array reassembled
    // by document.write in a NON-sequential index order (a[11] first).
    private const string ApplicantFragment = """
        <span id="ctl00_FeaturedContent_ApplicantLabel">KAELEY WISEMAN<br/>Telephone: <a href='tel:250-580-3835'>250-580-3835</a><br/>Email: <script type='text/javascript'>var a = new Array('EL','EY','@W','IS','ER','PR','OJ','EC','TS','.C','OM','KA');document.write("<a href='mailto:"+a[11]+a[0]+a[1]+a[2]+a[3]+a[4]+a[5]+a[6]+a[7]+a[8]+a[9]+a[10]+"'>"+a[11]+a[0]+a[1]+a[2]+a[3]+a[4]+a[5]+a[6]+a[7]+a[8]+a[9]+a[10]+"</a>");</script></span>
        """;

    private const string BodyFragment = """
        <span id="ctl00_FeaturedContent_ApplicationTypeLabel">Rezoning Application</span>
        <span id="ctl00_FeaturedContent_PurposeLabel">The City is considering a Rezoning application for a development consisting of three mixed- use towers with residential above commercial uses.

        CONCURRENT WITH DPV00297 APPLICATION. REFER TO REZONING FILE FOR APPLICATION MATERIALS.</span>
        <span id="ctl00_FeaturedContent_AddressesLabel">846 BROUGHTON ST 854 BROUGHTON ST 829 FORT ST</span>
        """;

    private const string DocumentsFragment = """
        <div id="ctl00_FeaturedContent_documentsContainer" class="documentsContainer">
          <div class="col-xs-12">
            <div><a href="javascript:scrollTo('sectionTop')" class="menuitem">Top</a></div>
            <div><a href="FileDownload.aspx?fileId=71941C250806113327488931&folderId=69772C250729104643138051">2025-08-06 - Letter to Mayor and Council.pdf</a></div>
            <div><a href="FileDownload.aspx?fileId=71941C250806114717349328&folderId=69772C250729104643138051">2025-08-06 - Plans_Submission_1of2</a></div>
            <div><a href="FileDownload.aspx?fileId=98837C251103141920350742&folderId=69772C250729104643138051">2025-11-03 - Plans_Revisions_Bubbled_1of2</a></div>
          </div>
        </div>
        """;

    private static string Page => ApplicantFragment + BodyFragment + DocumentsFragment;

    [Fact]
    public void TheApplicantsOwnAgentIsRead()
    {
        // The ArcGIS layer gives the CITY PLANNER. This is the other side of the
        // table — the person acting for the developer.
        var result = VictoriaProsperoLiveDetailExtractor.ParseDetail(Page, DetailUrl);

        Assert.NotNull(result);
        Assert.Equal("KAELEY WISEMAN", result!.ContactName);
        Assert.Equal("250-580-3835", result.ContactPhone);
    }

    [Fact]
    public void TheObfuscatedEmailIsReassembledInTheOrderThePageUses()
    {
        // a[11] first, then a[0..10] — read sequentially it would come out as
        // "ELEY@WISERPROJECTS.COMKA", a plausible-looking wrong address.
        var result = VictoriaProsperoLiveDetailExtractor.ParseDetail(Page, DetailUrl);

        Assert.Equal("KAELEY@WISERPROJECTS.COM", result!.ContactEmail);
    }

    [Fact]
    public void APlainMailtoIsPreferredWhenTheScriptHasAlreadyRun()
    {
        // Under Playwright document.write has executed, so the real page carries
        // an ordinary link and no array at all.
        const string rendered =
            """<span id="ctl00_FeaturedContent_ApplicantLabel">JANE ROE<br/>Email: <a href="mailto:jane@example.com">jane@example.com</a></span>""";

        var result = VictoriaProsperoLiveDetailExtractor.ParseDetail(rendered, DetailUrl);

        Assert.Equal("JANE ROE", result!.ContactName);
        Assert.Equal("jane@example.com", result.ContactEmail);
    }

    [Fact]
    public void ThePurposeAndEveryAddressSurvive()
    {
        var result = VictoriaProsperoLiveDetailExtractor.ParseDetail(Page, DetailUrl);

        Assert.Contains("three mixed- use towers", result!.Description);
        Assert.Contains("Rezoning Application", result.Description);
        Assert.Contains("829 FORT ST", result.Description);
    }

    [Fact]
    public void OnlyRealFileDownloadsAreCapturedAndTheyAreAbsolute()
    {
        var result = VictoriaProsperoLiveDetailExtractor.ParseDetail(Page, DetailUrl);

        Assert.Equal(3, result!.Documents.Count);
        Assert.DoesNotContain(result.Documents, d => d.Name == "Top");
        Assert.All(result.Documents, d =>
            Assert.StartsWith("https://tender.victoria.ca/webapps/ourcity/prospero/FileDownload.aspx", d.Url));
        Assert.Contains(result.Documents, d => d.Name == "2025-08-06 - Plans_Submission_1of2");
    }

    [Fact]
    public void APageWithNoApplicationBlockReturnsNullRatherThanAnEmptyShell()
    {
        Assert.Null(VictoriaProsperoLiveDetailExtractor.ParseDetail(
            "<html><body>Back to Search</body></html>", DetailUrl));
    }
}
