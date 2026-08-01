#nullable enable
using System;
using AngleSharp.Html.Parser;
using Kor.Opportunities.Data.Ingestion.Providers;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class CivicInfoHtmlOpportunityProviderTests
{
    [Fact]
    public void TryNormalizeUrl_WithAbsoluteHttps_ReturnsAbsolute()
    {
        var ok = CivicInfoHtmlOpportunityProvider.TryNormalizeUrl(
            new Uri("https://example.com/rfps/open"),
            "https://example.com/foo",
            out var absolute);

        Assert.True(ok);
        Assert.Equal("https://example.com/foo", absolute);
    }

    [Fact]
    public void TryNormalizeUrl_WithRelativePath_ResolvesAgainstBase()
    {
        var ok = CivicInfoHtmlOpportunityProvider.TryNormalizeUrl(
            new Uri("https://example.com/rfps/open"),
            "/foo",
            out var absolute);

        Assert.True(ok);
        Assert.Equal("https://example.com/foo", absolute);
    }

    [Fact]
    public void TryNormalizeUrl_WithFtp_ReturnsFalse()
    {
        var ok = CivicInfoHtmlOpportunityProvider.TryNormalizeUrl(
            new Uri("https://example.com/rfps/open"),
            "ftp://example.com/foo",
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void IsAllowedRequest_WithSameHostUnderListingPath_ReturnsTrue()
    {
        var baseUri = new Uri("https://example.com");
        var listingRoot = new Uri("https://example.com/rfps/open");
        var candidate = new Uri("https://example.com/rfps/open/123");

        var allowed = CivicInfoHtmlOpportunityProvider.IsAllowedRequest(baseUri, listingRoot, candidate);

        Assert.True(allowed);
    }

    [Fact]
    public void IsAllowedRequest_RejectsDifferentHostApiAndOutsideListingRoot()
    {
        var baseUri = new Uri("https://example.com");
        var listingRoot = new Uri("https://example.com/rfps/open");

        Assert.False(CivicInfoHtmlOpportunityProvider.IsAllowedRequest(
            baseUri,
            listingRoot,
            new Uri("https://other.example.com/rfps/open/123")));
        Assert.False(CivicInfoHtmlOpportunityProvider.IsAllowedRequest(
            baseUri,
            listingRoot,
            new Uri("https://example.com/rfps/open/api/items")));
        Assert.False(CivicInfoHtmlOpportunityProvider.IsAllowedRequest(
            baseUri,
            listingRoot,
            new Uri("https://example.com/archive/123")));
    }

    [Fact]
    public void AdvanceToNextPageUrl_WhenMorePages_ReturnsNextPageQuery()
    {
        var next = CivicInfoHtmlOpportunityProvider.AdvanceToNextPageUrl(
            new Uri("https://example.com/rfps/open"),
            page: 1,
            maxPages: 5);

        Assert.NotNull(next);
        Assert.Equal("https://example.com/rfps/open?page=2", next.ToString());
    }

    [Fact]
    public void AdvanceToNextPageUrl_WhenAtMaxPages_ReturnsNull()
    {
        var next = CivicInfoHtmlOpportunityProvider.AdvanceToNextPageUrl(
            new Uri("https://example.com/rfps/open"),
            page: 5,
            maxPages: 5);

        Assert.Null(next);
    }

    [Fact]
    public void AdvanceToNextPageUrl_WhenListingUrlHasQuery_UsesAmpersand()
    {
        var next = CivicInfoHtmlOpportunityProvider.AdvanceToNextPageUrl(
            new Uri("https://example.com/rfps/open?status=current"),
            page: 1,
            maxPages: 5);

        Assert.NotNull(next);
        Assert.Equal("https://example.com/rfps/open?status=current&page=2", next.ToString());
    }

    [Fact]
    public void SelectText_WithMatchingSelector_ReturnsText()
    {
        var document = new HtmlParser().ParseDocument("<article><h1>X</h1></article>");

        var text = CivicInfoHtmlOpportunityProvider.SelectText(document.DocumentElement, ["h1"]);

        Assert.Equal("X", text);
    }

    [Fact]
    public void SelectLink_WithAnchorSelector_ReturnsHrefAttribute()
    {
        var document = new HtmlParser().ParseDocument("""<article><a href="/foo">X</a></article>""");

        var href = CivicInfoHtmlOpportunityProvider.SelectLink(document.DocumentElement, ["a"]);

        Assert.Equal("/foo", href);
    }
}
