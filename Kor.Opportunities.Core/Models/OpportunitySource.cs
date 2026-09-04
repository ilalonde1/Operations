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
    // Manually-imported BD relationship outreach (no automated polling).
    BdOutreach = 7,
    BcBid = 8,
    BcBidAwards = 9,
    BidsAndTenders = 10,
    AlbertaPurchasingConnection = 11,
    BidsAndTendersAwards = 12,
    AlbertaPurchasingConnectionAwards = 13,
    BcBidUnverifiedBidResults = 14,
    BcBidHistorical = 15,
    GenericCsvAward = 16,
    GenericJsonAward = 17,
    MajorProjectsInventory = 18,
    // MERX public DCC solicitations listing (Playwright; WAF blocks plain HTTP).
    MerxDcc = 19,
    // ArcGIS Feature/Map Server layer query (2026-09-03). One adapter for the
    // whole ArcGIS Hub / ArcGIS Open Data platform, which is what most BC
    // municipalities and regional districts publish through — so a new city is
    // a config row, not a scraper. Carries EARLY signal (development permit and
    // rezoning APPLICATIONS), unlike the tender feeds which arrive after the
    // structural engineer has already been chosen.
    ArcGisFeatureService = 20,
    // Tempest "OurCity / Prospero" development tracker (2026-09-04). Victoria,
    // Saanich and View Royal all license it, and the detail pages are identical
    // enough that one extractor already reads them all. This is the LISTING
    // side: ASP.NET WebForms, paged by partial postback.
    TempestProspero = 21,
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

    /// <summary>
    /// When true, the IngestionService routes this source's candidates into the
    /// HistoricalOpportunities/HistoricalOpportunityObservations tables instead of
    /// the active pipeline. Archive-only sources (e.g. BcBidHistorical) should set this.
    /// </summary>
    public bool IsHistorical { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
