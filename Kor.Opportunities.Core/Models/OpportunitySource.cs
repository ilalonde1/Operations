#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// Provider type for an <see cref="OpportunitySource"/>. Stable on disk —
/// values are persisted in <c>opportunities.OpportunitySources.SourceType</c>.
/// </summary>
public enum OpportunitySourceType
{
    Unknown = 0,
    GenericCsv = 1,
    GenericJson = 2,
    Rss = 3,
    CivicInfoHtml = 4,
    GraphEmail = 5,
    SamGov = 6,
    BcBid = 8,
    // Manually-imported BD relationship outreach (no automated polling).
    BdOutreach = 7,
    Manual = 99,
}

/// <summary>
/// One configured ingestion provider. Mirrors <c>opportunities.OpportunitySources</c>.
/// Per-source key/value config (column mappings, filters, etc.) lives in
/// <see cref="OpportunitySourceMapping"/> rows keyed on <c>OpportunitySourceId</c>.
/// </summary>
public sealed record OpportunitySource
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public OpportunitySourceType SourceType { get; init; } = OpportunitySourceType.Unknown;

    public string BaseUrl { get; init; } = "";

    public bool IsEnabled { get; init; } = true;

    public int CrawlDelaySeconds { get; init; } = 1800;

    public int RequestTimeoutSeconds { get; init; } = 30;

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
