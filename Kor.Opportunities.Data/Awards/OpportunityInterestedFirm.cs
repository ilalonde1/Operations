#nullable enable
using System;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// Per-opportunity expressed-interest firm record. One row per firm that
/// registered interest in a procurement notice via the source portal's
/// "Expressed Interest" / "Plan-Takers" / "Bidder List" feature.
///
/// Captured by source-specific scrapers (APC, BC Bid, MERX, Bonfire, etc.)
/// and resolved to a CanonicalOrgId via CanonicalOrgResolver so the firm
/// surfaces on architect/competitor dossiers.
/// </summary>
public sealed record OpportunityInterestedFirm(
    long Id,
    long OpportunityId,
    string RawFirmName,
    long? ResolvedCanonicalOrgId,
    string? ResolvedKind,
    string SourcePortal,
    string? SourcePostingUrl,
    DateTimeOffset? ExpressedAtUtc,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string? Notes,
    string? RawJson);
