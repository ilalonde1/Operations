using System;
using System.IO;
using Kor.Opportunities.Data.Portals;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Exercises the pure parts of the APC signed-in download: posting URL to feed path, the captured Banff Centre feed
// (AB-2026-06968) to the ids a download needs, the token answer, the download-link answer, and what the page stores after a
// sign-in. Does not call APC or the sign-in server; a change in the az-sas-uri route or its query values, or in the key
// oidc-client-ts stores the user under, would not be caught here.
public sealed class ApcPortalTests
{
    private static string Feed()
    {
        using var stream = typeof(ApcPortalTests).Assembly.GetManifestResourceStream(
            "Kor.Opportunities.Data.Tests.Fixtures.apc-posting-AB-2026-06968-20261020.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_posting_url_maps_to_its_public_feed_and_nothing_else_does()
    {
        Assert.Equal("/opportunity/public/2026/6968", ApcPortal.PostingPath("https://purchasing.alberta.ca/posting/AB-2026-06968"));
        Assert.Equal("/opportunity/public/2026/617", ApcPortal.PostingPath("https://purchasing.alberta.ca/posting/AB-2026-00617#document-x"));
        Assert.Null(ApcPortal.PostingPath("https://leduc.bidsandtenders.ca/Module/Tenders/en/Tender/Detail/270f022c-1f1e-4b63-8944-2a1837cae96f"));
        Assert.Null(ApcPortal.PostingPath(""));
    }

    [Fact]
    public void The_banff_feed_gives_the_opportunity_id_and_every_document_with_its_file_name()
    {
        var p = ApcPortal.ParsePosting(Feed())!;
        Assert.Equal(Guid.Parse("34b3885e-c719-41dc-a877-ecbe542f78ac"), p.OpportunityId);
        Assert.Equal(3, p.Documents.Count);
        Assert.Equal(Guid.Parse("075efe00-3c7e-4cdc-8758-95f1b177c43b"), p.Documents[0].Id);
        Assert.Equal("RFP# PF-1026-02.pdf", p.Documents[0].FileName);
        Assert.Equal(1193698, p.Documents[0].SizeBytes);
        Assert.Contains(p.Documents, d => d.FileName == "Appendix C - Rate Bid Form.xlsx");
        Assert.Contains(p.Documents, d => d.FileName == "Appendix D - Past Performance Questionnaire.pdf");
    }

    [Fact]
    public void A_token_answer_gives_both_tokens_and_their_lifetimes()
    {
        var now = new DateTimeOffset(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);
        var t = ApcPortal.ParseTokens("""{"access_token":"A","expires_in":300,"refresh_expires_in":1800,"refresh_token":"R","token_type":"Bearer"}""", now);
        Assert.Equal("A", t.AccessToken);
        Assert.Equal("R", t.RefreshToken);
        Assert.Equal(now.AddMinutes(5), t.AccessExpiresUtc);
        Assert.Equal(now.AddMinutes(30), t.RefreshExpiresUtc);
        Assert.Null(ApcPortal.ParseTokens("""{"access_token":"A","expires_in":300,"refresh_expires_in":0,"refresh_token":"R"}""", now).RefreshExpiresUtc);
        Assert.Throws<FormatException>(() => ApcPortal.ParseTokens("""{"access_token":"A","expires_in":300}""", now));
    }

    [Fact]
    public void The_download_link_is_read_from_a_bare_link_a_json_string_or_an_object()
    {
        const string link = "https://apcprod.blob.core.windows.net/docs/x.pdf?sv=2024&sig=abc";
        Assert.Equal(link, ApcPortal.ParseSasUri(link));
        Assert.Equal(link, ApcPortal.ParseSasUri("\"" + link + "\""));
        Assert.Equal(link, ApcPortal.ParseSasUri("{\"sasUri\":\"" + link + "\",\"title\":\"x\"}"));
        Assert.Null(ApcPortal.ParseSasUri("{\"error\":\"nope\"}"));
    }

    [Fact]
    public void A_signed_in_page_gives_its_refresh_token_and_account_and_a_signed_out_page_gives_nothing()
    {
        var s = ApcPortal.ReadBrowserSession("""{"access_token":"A","refresh_token":"R","profile":{"email":"bdalerts@korstructural.com","sub":"1"},"expires_at":1792605000}""")!;
        Assert.Equal("R", s.RefreshToken);
        Assert.Equal("bdalerts@korstructural.com", s.Account);
        Assert.Null(ApcPortal.ReadBrowserSession(""));
        Assert.Null(ApcPortal.ReadBrowserSession("""{"access_token":"A","profile":{}}"""));
    }
}
