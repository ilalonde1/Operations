#nullable enable
using System.Collections.Generic;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class BcBidScraperTests
{
    [Fact]
    public void ResolveKeywordFilter_WithConfiguredEngineeringKeyword_ReturnsTrimmedKeyword()
    {
        var sourceConfig = new Dictionary<string, string>
        {
            [BcBidScraper.KeywordConfigKey] = " engineering ",
        };

        var keyword = BcBidScraper.ResolveKeywordFilter(sourceConfig);

        Assert.Equal("engineering", keyword);
    }

    [Fact]
    public void ResolveKeywordFilter_WithBlankKeyword_ReturnsNull()
    {
        var sourceConfig = new Dictionary<string, string>
        {
            [BcBidScraper.KeywordConfigKey] = " ",
        };

        var keyword = BcBidScraper.ResolveKeywordFilter(sourceConfig);

        Assert.Null(keyword);
    }
}
