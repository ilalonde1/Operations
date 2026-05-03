#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// One CRM engagement — created when we decide to pursue an
/// <see cref="Opportunity"/> and tracked through win / lose / withdraw.
/// Mirrors <c>opportunities.CrmEngagements</c>. RowVersion drives optimistic
/// concurrency on every update path.
/// </summary>
public sealed record CrmEngagement
{
    public long Id { get; init; }

    public long OpportunityId { get; init; }

    public CrmEngagementStage Stage { get; init; } = CrmEngagementStage.Pursuing;

    public string? OwnerStaffId { get; init; }

    /// <summary>Comma-separated list of staff IDs assigned besides the owner.</summary>
    public string? AssignedStaffIds { get; init; }

    /// <summary>Target margin as a percent (e.g. 22.5 for 22.5 %).</summary>
    public decimal? TargetMargin { get; init; }

    public decimal? ProposedFee { get; init; }

    public decimal? ProposedHours { get; init; }

    public string? Notes { get; init; }

    public DateTimeOffset OpenedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClosedAtUtc { get; init; }

    public string? OutcomeNotes { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string CreatedBy { get; init; } = "";

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string UpdatedBy { get; init; } = "";

    public byte[] RowVersion { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// CRM-side stage. Distinct from <see cref="OpportunityStatus"/> because the
/// CRM tracks the *engagement* lifecycle (proposal-draft -> presenting -> ...)
/// while OpportunityStatus tracks the *pursuit* lifecycle (identified -> qualified -> ...).
/// Stable on disk; values map to opportunities.CrmEngagements.Stage.
/// </summary>
public enum CrmEngagementStage
{
    Pursuing = 1,
    ProposalDraft = 2,
    ProposalSubmitted = 3,
    Presenting = 4,
    Negotiating = 5,
    Won = 6,
    Lost = 7,
    Withdrawn = 8,
    OnHold = 9,
}
