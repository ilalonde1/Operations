#nullable enable
using System.Collections.Generic;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class BcBidUnverifiedBidResultsScraperTests
{
    [Theory]
    [MemberData(nameof(ObservedLayouts))]
    public void MapBidCells_WithObservedBcTimberSalesLayouts_MapsBidderContent(
        IReadOnlyList<string> cells,
        string externalReference,
        string expectedBidderName,
        string expectedAddress,
        decimal expectedAmount,
        int? expectedRank)
    {
        var mapping = BcBidUnverifiedBidResultsScraper.MapBidCells(cells, externalReference);

        Assert.Equal(expectedBidderName, mapping.BidderName);
        Assert.Equal(expectedAddress, mapping.BidderAddress);
        Assert.Equal(expectedAmount, mapping.BidAmount);
        Assert.Equal(expectedRank, mapping.BidderRank);
    }

    [Fact]
    public void MapBidCells_WithNotPubliclyDisclosedBidder_ReturnsNullName()
    {
        var mapping = BcBidUnverifiedBidResultsScraper.MapBidCells(
            DetailCells("1117", "not publicly disclosed", "Kamloops, British Columbia", "$ 88,100.00"),
            "1117");

        Assert.Null(mapping.BidderName);
        Assert.Equal("Kamloops, British Columbia", mapping.BidderAddress);
        Assert.Equal(88100.00m, mapping.BidAmount);
    }

    public static TheoryData<IReadOnlyList<string>, string, string, string, decimal, int?> ObservedLayouts()
        => new()
        {
            {
                DetailCells("1115", "Prince George, British Columbia", "FORSITE CONSULTANTS LTD.", "$ 42,000.00"),
                "1115",
                "FORSITE CONSULTANTS LTD.",
                "Prince George, British Columbia",
                42000.00m,
                null
            },
            {
                DetailCells("1120", "LITTLE BIG WORKS REVELSTOKE LTD.", "Revelstoke, British Columbia", "$ 154,875.00"),
                "1120",
                "LITTLE BIG WORKS REVELSTOKE LTD.",
                "Revelstoke, British Columbia",
                154875.00m,
                null
            },
            {
                DetailCells("1116", "$ 379,361.50  1", "Kamloops, British Columbia", "INTEGRATED PROACTION CORP."),
                "1116",
                "INTEGRATED PROACTION CORP.",
                "Kamloops, British Columbia",
                379361.50m,
                1
            },
        };

    private static IReadOnlyList<string> DetailCells(
        string opportunityId,
        string supplierCell5,
        string supplierCell6,
        string supplierCell7)
        => new[]
        {
            opportunityId,
            "BC Timber Sales road maintenance",
            "BC Timber Sales Branch",
            "2026-05-01 14:00:00 (PDT)",
            "2026-05-02 09:00:00 (PDT)",
            supplierCell5,
            supplierCell6,
            supplierCell7,
        };
}
