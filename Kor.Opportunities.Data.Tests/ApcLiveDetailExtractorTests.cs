using Kor.Opportunities.Data.Ingestion.Scraping;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// Exercises the pure feed parser on a captured APC posting feed (Banff Centre, AB-2026-06968, trimmed to the posting and
// its documents). Does not cover navigation, Cloudflare, or the job's persistence; a change in the feed's URL path that
// stops the page's request from matching would not be caught here.
public sealed class ApcLiveDetailExtractorTests
{
    private const string Url = "https://purchasing.alberta.ca/posting/AB-2026-06968";

    private static string Feed()
    {
        using var stream = typeof(ApcLiveDetailExtractorTests).Assembly.GetManifestResourceStream(
            "Kor.Opportunities.Data.Tests.Fixtures.apc-posting-AB-2026-06968-20261020.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Feed_GivesEveryDocument_WithAUniqueUrlCarryingItsId()
    {
        var r = ApcLiveDetailExtractor.ParseFeed(Feed(), Url)!;
        Assert.Equal(3, r.Documents.Count);
        Assert.Equal("RFP# PF-1026-02.pdf", r.Documents[0].Name);
        Assert.Equal(Url + "#document-075efe00-3c7e-4cdc-8758-95f1b177c43b", r.Documents[0].Url);
        Assert.Contains(r.Documents, d => d.Name.StartsWith("Appendix C - Rate Bid Form", StringComparison.Ordinal));
        Assert.Contains(r.Documents, d => d.Name.StartsWith("Appendix D - Past Performance Questionnaire", StringComparison.Ordinal));
        Assert.Equal(3, r.Documents.Select(d => d.Url).Distinct().Count());
    }

    [Fact]
    public void Feed_GivesTheContactQuestionsDeadlineAndSubmissionMethod()
    {
        var r = ApcLiveDetailExtractor.ParseFeed(Feed(), Url)!;
        Assert.Equal("Amaya Kingston, Procurement Coordinator", r.ContactName);
        Assert.Equal("amaya_kingston@banffcentre.ca", r.ContactEmail);
        Assert.Equal("(403) 762-6111", r.ContactPhone);
        Assert.StartsWith("We are inviting qualified vendors", r.Description);
        Assert.Contains("Buyer reference: RFP# PF-1026-02", r.Description);
        Assert.Contains("Questions by Oct 23, 2026 2:00 PM (MT)", r.Description);
        Assert.Contains("Submission: email to amaya_kingston@banffcentre.ca", r.Description);
    }

    [Fact]
    public void Feed_WithoutAnOpportunity_IsNull_AndADeletedDocumentIsSkipped()
    {
        Assert.Null(ApcLiveDetailExtractor.ParseFeed("{}", Url));
        Assert.Null(ApcLiveDetailExtractor.ParseFeed("", Url));
        var deleted = Feed().Replace("\"deletedOnUtc\": null", "\"deletedOnUtc\": \"2026-10-20T00:00:00Z\"");
        Assert.Empty(ApcLiveDetailExtractor.ParseFeed(deleted, Url)!.Documents);
    }
}
