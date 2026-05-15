#nullable enable
using System;
using System.Xml.Linq;
using Kor.Opportunities.Data.Ingestion.Providers;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class RssOpportunityProviderTests
{
    [Fact]
    public void ParseRssItems_WithTwoItems_ReturnsCandidates()
    {
        var doc = XDocument.Parse("""
            <rss version="2.0">
              <channel>
                <item>
                  <title>RFP One - Buyer One</title>
                  <link>https://example.com/one</link>
                  <description>First notice</description>
                  <pubDate>Fri, 15 May 2026 12:00:00 GMT</pubDate>
                  <guid>one</guid>
                </item>
                <item>
                  <title>RFP Two - Buyer Two</title>
                  <link>https://example.com/two</link>
                  <description>Second notice</description>
                  <pubDate>Fri, 15 May 2026 13:00:00 GMT</pubDate>
                  <guid>two</guid>
                </item>
              </channel>
            </rss>
            """);

        var candidates = RssOpportunityProvider.ParseRssItems(doc, "https://example.com/rss");

        Assert.Equal(2, candidates.Count);
        Assert.Equal("RFP One - Buyer One", candidates[0].Title);
        Assert.Equal("https://example.com/one", candidates[0].Url);
        Assert.NotNull(candidates[0].PostedDateUtc);
    }

    [Fact]
    public void ParseAtomEntries_WithTwoEntries_ReturnsCandidates()
    {
        var doc = XDocument.Parse("""
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>Atom One - Buyer One</title>
                <link href="https://example.com/atom-one" />
                <summary>First atom notice</summary>
                <published>2026-05-15T12:00:00Z</published>
                <id>atom-one</id>
              </entry>
              <entry>
                <title>Atom Two - Buyer Two</title>
                <link href="https://example.com/atom-two" />
                <summary>Second atom notice</summary>
                <published>2026-05-15T13:00:00Z</published>
                <id>atom-two</id>
              </entry>
            </feed>
            """);

        var candidates = RssOpportunityProvider.ParseAtomEntries(doc, "https://example.com/feed");

        Assert.Equal(2, candidates.Count);
        Assert.Equal("Atom One - Buyer One", candidates[0].Title);
        Assert.Equal("https://example.com/atom-one", candidates[0].Url);
    }

    [Fact]
    public void ParseRssItems_WithEmptyTitle_SkipsItem()
    {
        var doc = XDocument.Parse("""
            <rss version="2.0"><channel><item><title> </title><link>https://example.com/one</link></item></channel></rss>
            """);

        var candidates = RssOpportunityProvider.ParseRssItems(doc, "https://example.com/rss");

        Assert.Empty(candidates);
    }

    [Fact]
    public void ParseRssItems_WithEmptyLink_SkipsItem()
    {
        var doc = XDocument.Parse("""
            <rss version="2.0"><channel><item><title>Notice</title><link> </link></item></channel></rss>
            """);

        var candidates = RssOpportunityProvider.ParseRssItems(doc, "https://example.com/rss");

        Assert.Empty(candidates);
    }

    [Fact]
    public void TryNormalizeUrl_HandlesRelativeAbsoluteAndRejectedSchemes()
    {
        var relativeOk = RssOpportunityProvider.TryNormalizeUrl("/notices/123", "https://example.com/rss", out var relativeUrl);
        var absoluteOk = RssOpportunityProvider.TryNormalizeUrl("https://other.com/foo", "https://example.com/rss", out var absoluteUrl);
        var badOk = RssOpportunityProvider.TryNormalizeUrl("ftp://example.com/foo", "https://example.com/rss", out _);

        Assert.True(relativeOk);
        Assert.Equal("https://example.com/notices/123", relativeUrl);
        Assert.True(absoluteOk);
        Assert.Equal("https://other.com/foo", absoluteUrl);
        Assert.False(badOk);
    }

    [Fact]
    public void ExtractBuyerFromTitle_WithDashSeparator_ReturnsRightHandSide()
    {
        var buyer = RssOpportunityProvider.ExtractBuyerFromTitle("Project Alpha - City of Testville");

        Assert.Equal("City of Testville", buyer);
    }

    [Fact]
    public void ExtractBuyerFromTitle_WithPipeSeparator_ReturnsRightHandSide()
    {
        var buyer = RssOpportunityProvider.ExtractBuyerFromTitle("Project Beta | Regional District");

        Assert.Equal("Regional District", buyer);
    }

    [Fact]
    public void ExtractBuyerFromTitle_WithNoSeparator_ReturnsUnknown()
    {
        var buyer = RssOpportunityProvider.ExtractBuyerFromTitle("Project Gamma");

        Assert.Equal("Unknown", buyer);
    }

    [Fact]
    public void ParseAtomEntries_WithLinkHref_ExtractsAttributeValue()
    {
        var doc = XDocument.Parse("""
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>Atom Link - Buyer</title>
                <link href="https://example.com/from-href">ignored text</link>
                <summary>Summary</summary>
                <id>atom-link</id>
              </entry>
            </feed>
            """);

        var candidates = RssOpportunityProvider.ParseAtomEntries(doc, "https://example.com/feed");

        Assert.Single(candidates);
        Assert.Equal("https://example.com/from-href", candidates[0].Url);
    }

    [Fact]
    public void ParseRssItems_WithBadUrl_SkipsItem()
    {
        var doc = XDocument.Parse("""
            <rss version="2.0"><channel><item><title>Notice</title><link>ftp://example.com/one</link></item></channel></rss>
            """);

        var candidates = RssOpportunityProvider.ParseRssItems(doc, "https://example.com/rss");

        Assert.Empty(candidates);
    }
}
