#nullable enable
using System;
using Kor.Opportunities.Core.Ingestion.EmailAdapters;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class GenericEmailFormatAdapterTests
{
    private readonly GenericEmailFormatAdapter _adapter = new();

    [Fact]
    public void Parse_WithPlainTextHttpsUrl_ReturnsCandidate()
    {
        var message = CreateMessage(subject: "New RFP", body: "Please review https://example.com/rfp");

        var candidate = _adapter.Parse(message);

        Assert.NotNull(candidate);
        Assert.Equal("https://example.com/rfp", candidate.Url);
        Assert.Equal("New RFP", candidate.Title);
        Assert.Equal("Unknown", candidate.Buyer);
    }

    // 2026-10-22: all 100 rows the BD alerts mailbox had made were a portal's own mail, and the newest APC digest headed
    // the Opportunities list. Each subject below is one of those kinds, copied from the rows; none may become a candidate.
    [Theory]
    [InlineData("APC - Notification of New Postings for \"KOR Structural AB - daily\" - Wed, Oct 21")]
    [InlineData("APC Opportunity AB-2026-04147 Awarded")]
    [InlineData("APC Opportunity AB-2026-03317 Amended")]
    [InlineData("Bidders list  Posted for APC Opportunity AB-2026-04147")]
    [InlineData("Bidders and Pre-qualified Suppliers List")]
    [InlineData("Your APC Account Sign-in Email Has Changed")]
    [InlineData("Your APC Supplier Account Role Has Been Updated")]
    [InlineData("Test RFP From Acme BC Inc")]
    public void Parse_APortalsOwnMail_IsNotAnOpportunity(string subject)
        => Assert.Null(_adapter.Parse(CreateMessage(subject: subject, body: "See https://purchasing.alberta.ca/posting/AB-2026-04147")));

    [Fact]
    public void Parse_AnRfpThatMentionsAPostingNumber_IsStillAnOpportunity()
        => Assert.NotNull(_adapter.Parse(CreateMessage(subject: "RFP AB-2026-04147 Prime Consultant Services - Peace River Pool", body: "https://purchasing.alberta.ca/posting/AB-2026-04147")));

    [Fact]
    public void Parse_WithHtmlHref_ExtractsUrlFromHref()
    {
        var message = CreateMessage(subject: "HTML RFP", body: """<p>Review <a href="https://example.com/html">details</a></p>""");

        var candidate = _adapter.Parse(message);

        Assert.NotNull(candidate);
        Assert.Equal("https://example.com/html", candidate.Url);
    }

    [Fact]
    public void Parse_WithHtmlEntities_DecodesDescription()
    {
        var message = CreateMessage(subject: "Entity RFP", body: "Tom &amp; Co https://example.com/entity");

        var candidate = _adapter.Parse(message);

        Assert.NotNull(candidate);
        Assert.Contains("Tom & Co", candidate.Description);
    }

    [Fact]
    public void Parse_WithMultipleHttpsUrls_UsesFirst()
    {
        var message = CreateMessage(subject: "Links", body: "First https://example.com/one second https://example.com/two");

        var candidate = _adapter.Parse(message);

        Assert.NotNull(candidate);
        Assert.Equal("https://example.com/one", candidate.Url);
    }

    [Fact]
    public void Parse_WithHttpOnly_ReturnsNull()
    {
        var message = CreateMessage(subject: "HTTP", body: "Only http://example.com/rfp");

        var candidate = _adapter.Parse(message);

        Assert.Null(candidate);
    }

    [Fact]
    public void Parse_WithNoUrl_ReturnsNull()
    {
        var message = CreateMessage(subject: "No URL", body: "No link here.");

        var candidate = _adapter.Parse(message);

        Assert.Null(candidate);
    }

    [Fact]
    public void Parse_WithCompanyLine_UsesCompanyAsBuyer()
    {
        var message = CreateMessage(subject: "RFP", body: "Company: Acme Corp\nSee https://example.com/acme");

        var candidate = _adapter.Parse(message);

        Assert.NotNull(candidate);
        Assert.Equal("Acme Corp", candidate.Buyer);
    }

    [Fact]
    public void Parse_WithSubjectPrefixFallback_UsesPrefixAsBuyer()
    {
        var message = CreateMessage(subject: "Acme Corp - New RFP", body: "See https://example.com/acme");

        var candidate = _adapter.Parse(message);

        Assert.NotNull(candidate);
        Assert.Equal("Acme Corp", candidate.Buyer);
    }

    [Fact]
    public void Parse_WithEmptySubject_UsesBodyFirstLineAsTitle()
    {
        var message = CreateMessage(subject: "", body: "Project X\nhttps://example.com/project-x");

        var candidate = _adapter.Parse(message);

        Assert.NotNull(candidate);
        Assert.Equal("Project X", candidate.Title);
    }

    [Fact]
    public void Parse_WithTrailingUrlPunctuation_StripsPunctuation()
    {
        var message = CreateMessage(subject: "Trailing", body: "See https://example.com/foo.");

        var candidate = _adapter.Parse(message);

        Assert.NotNull(candidate);
        Assert.Equal("https://example.com/foo", candidate.Url);
    }

    private static EmailMessage CreateMessage(string? subject, string? body) =>
        new(
            MessageId: Guid.NewGuid().ToString("N"),
            SenderAddress: "sender@example.com",
            Subject: subject,
            BodyHtmlOrPlain: body,
            ReceivedUtc: DateTimeOffset.Parse("2026-05-15T12:00:00Z"));
}
