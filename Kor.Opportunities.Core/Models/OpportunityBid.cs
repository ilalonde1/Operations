#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// One bidder-level result for a source opportunity. Mirrors
/// opportunities.OpportunityBids and stays separate from formal awards while
/// unverified bid-opening detail is still pre-award intelligence.
/// </summary>
public sealed record OpportunityBid
{
    public long Id { get; init; }

    public Guid OpportunitySourceId { get; init; }

    public string ExternalReference { get; init; } = "";

    public string BidderName { get; init; } = "";

    public long? BidderCanonicalOrgId { get; init; }

    public decimal? BidAmount { get; init; }

    public string BidCurrency { get; init; } = "CAD";

    public int? BidderRank { get; init; }

    public string? BidderAddress { get; init; }

    public string? SourceUrl { get; init; }

    public string? RawJson { get; init; }

    public Guid? IngestionRunId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public byte[] RowVersion { get; init; } = Array.Empty<byte>();
}
