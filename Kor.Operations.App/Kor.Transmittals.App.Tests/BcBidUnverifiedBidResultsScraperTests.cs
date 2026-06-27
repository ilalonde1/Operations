#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task CreateOpportunityBidAsync_WithResolver_SetsBidderCanonicalOrgId()
    {
        var resolver = new CanonicalOrgResolver(
            new FakeCanonicalOrgStore("LITTLE BIG WORKS REVELSTOKE LTD.", 4242),
            NullLogger<CanonicalOrgResolver>.Instance);
        var source = new OpportunitySource
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "BC Bid Unverified",
            SourceType = OpportunitySourceType.BcBidUnverifiedBidResults,
            BaseUrl = "https://bcbid.gov.bc.ca/page.aspx/en/bpm/process_manage_extranet/123",
        };

        var bid = await BcBidUnverifiedBidResultsScraper.CreateOpportunityBidAsync(
            DetailCells("1120", "LITTLE BIG WORKS REVELSTOKE LTD.", "Revelstoke, British Columbia", "$ 154,875.00"),
            source,
            "1120",
            "https://bcbid.gov.bc.ca/detail/1120",
            resolver,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.NotNull(bid);
        Assert.Equal("LITTLE BIG WORKS REVELSTOKE LTD.", bid.BidderName);
        Assert.Equal(4242, bid.BidderCanonicalOrgId);
        Assert.Equal("1120|BC Timber Sales road maintenance|BC Timber Sales Branch|2026-05-01 14:00:00 (PDT)|2026-05-02 09:00:00 (PDT)|LITTLE BIG WORKS REVELSTOKE LTD.|Revelstoke, British Columbia|$ 154,875.00", bid.RawJson);
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

    private sealed class FakeCanonicalOrgStore : ICanonicalOrgStore
    {
        private readonly string _rawName;
        private readonly long _canonicalOrgId;

        public FakeCanonicalOrgStore(string rawName, long canonicalOrgId)
        {
            _rawName = rawName;
            _canonicalOrgId = canonicalOrgId;
        }

        public Task<long> UpsertCanonicalOrgAsync(
            string kind,
            string displayName,
            string? clendorClientId,
            string? website,
            string? notes,
            CancellationToken ct)
            => Task.FromResult(_canonicalOrgId);

        public Task<CanonicalOrgRow?> GetCanonicalOrgAsync(long id, CancellationToken ct)
            => Task.FromResult<CanonicalOrgRow?>(null);

        public Task<IReadOnlyList<CanonicalOrgRow>> SearchCanonicalOrgsAsync(
            string? query,
            string? kind,
            int take,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CanonicalOrgRow>>(Array.Empty<CanonicalOrgRow>());

        public Task<IReadOnlyList<CanonicalOrgRow>> SearchCanonicalOrgsWithRelationshipsAsync(
            string? query,
            string? kind,
            int take,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CanonicalOrgRow>>(Array.Empty<CanonicalOrgRow>());

        public Task<CanonicalOrgRow?> GetCanonicalOrgByClendorIdAsync(string clendorClientId, CancellationToken ct)
            => Task.FromResult<CanonicalOrgRow?>(null);

        public Task RecordBcRegistrySnapshotAsync(long canonicalOrgId, BcRegistrySnapshot snapshot, CancellationToken ct)
            => Task.CompletedTask;

        public Task<(string DisplayName, string Kind)?> GetNameAndKindAsync(long canonicalOrgId, CancellationToken ct)
            => Task.FromResult<(string DisplayName, string Kind)?>(null);

        public Task<bool> UnretireAsync(long canonicalOrgId, string reason, CancellationToken ct)
            => Task.FromResult(false);

        public Task MarkRetiredOnIntakeAsync(long canonicalOrgId, string reason, CancellationToken ct)
            => Task.CompletedTask;

        public Task<long?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct)
            => Task.FromResult<long?>(null);

        public Task<long?> FindByFuzzyNormalizedNameAsync(string fuzzyKey, CancellationToken ct)
            => Task.FromResult<long?>(null);

        public Task<long?> ResolveCanonicalOrgMergeAsync(long canonicalOrgId, CancellationToken ct)
            => Task.FromResult<long?>(null);

        public Task<long?> FindMergedSurvivorByAliasAsync(string rawName, string source, CancellationToken ct)
            => Task.FromResult<long?>(null);

        public Task<long?> FindMergedSurvivorByNormalizedNameAsync(string normalizedName, CancellationToken ct)
            => Task.FromResult<long?>(null);

        public Task<int> CountRetiredMatchesAsync(string rawName, string source, string normalizedName, string fuzzyKey, CancellationToken ct)
            => Task.FromResult(0);

        public Task PromoteCanonicalOrgWebsiteNotesAsync(long canonicalOrgId, string? website, string? notes, CancellationToken ct)
            => Task.CompletedTask;

        public Task<long> UpsertAliasAsync(
            string rawName,
            string source,
            long? canonicalOrgId,
            int confidence,
            string? classifiedBy,
            string? notes,
            CancellationToken ct)
            => Task.FromResult(1L);

        public Task<OrgAliasRow?> LookupAliasAsync(string rawName, string source, CancellationToken ct)
        {
            if (string.Equals(rawName, _rawName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(source, "BcBidUnverified.Bidder", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<OrgAliasRow?>(new OrgAliasRow(
                    Id: 1,
                    CanonicalOrgId: _canonicalOrgId,
                    RawName: rawName,
                    Source: source,
                    Confidence: 95,
                    ClassifiedBy: "test",
                    ClassifiedAtUtc: DateTimeOffset.UtcNow,
                    Notes: null,
                    CreatedAtUtc: DateTimeOffset.UtcNow));
            }

            return Task.FromResult<OrgAliasRow?>(null);
        }

        public Task<IReadOnlyList<OrgAliasRow>> ListUnclassifiedAsync(string? source, int batchSize, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<OrgAliasRow>>(Array.Empty<OrgAliasRow>());

        public Task<(int Total, int Classified, int Unclassified)> GetAliasCountsAsync(CancellationToken ct)
            => Task.FromResult((0, 0, 0));
    }
}
