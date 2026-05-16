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

    public CrmEngagementStage Stage { get; init; } = CrmEngagementStage.Drafting;

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
/// CRM-side stage. Distinct from <see cref="OpportunityStatus"/>: this enum
/// tracks the *engagement's proposal-work* state (drafting -> submitted -> outcome)
/// while OpportunityStatus tracks the *opportunity's lifecycle* (new -> pursuing -> submitted -> outcome).
/// Stable on disk; values map to opportunities.CrmEngagements.Stage.
/// Collapsed 2026-05-15 from 9 to 4: Pursuing+ProposalDraft+OnHold -> Drafting;
/// ProposalSubmitted+Presenting+Negotiating -> Submitted; Withdrawn -> Lost
/// (note preserved in OutcomeNotes).
/// </summary>
public enum CrmEngagementStage
{
    Drafting = 1,    // was Pursuing(1), ProposalDraft(2), OnHold(9) pre-2026-05-15
    Submitted = 3,   // was ProposalSubmitted(3), Presenting(4), Negotiating(5)
    Won = 6,
    Lost = 7,        // was Lost(7), Withdrawn(8)
}
