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

    /// <summary>
    /// FK to <see cref="Opportunity"/>. Null for BD-tracking engagements
    /// (touchpoints with no formal RFP behind them — meeting at a UDI tour,
    /// email about potential hotel work) per migration 48.
    /// </summary>
    public long? OpportunityId { get; init; }

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

    // ===== BD-tracking columns (migration 48) =====
    //
    // Populated by tools/BdTrackingImport when an engagement is a BD touchpoint
    // (OpportunityId IS NULL); null on opportunity-linked engagements.

    /// <summary>FK to CanonicalOrg(Id) — the firm/buyer this engagement is with.</summary>
    public long? BuyerCanonicalOrgId { get; init; }

    /// <summary>
    /// BD-tracking region — one of: Vancouver/LowerMainland, VancouverIsland,
    /// Alberta, Okanagan-BcInterior, USA, EasternCanada. Mirrors the
    /// spreadsheet's regional sheets.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>Sum of proposal bids submitted on this engagement (CAD).</summary>
    public decimal? ProposalsSubmittedCad { get; init; }

    /// <summary>Sum of accepted/won proposal values (CAD).</summary>
    public decimal? ProposalsAcceptedCad { get; init; }

    /// <summary>
    /// Freeform text from the spreadsheet's "Potential Projects" column —
    /// what the BD initiator discussed with the contact. Cross-link tool
    /// fuzzy-matches this against MajorProjectsInventory project names.
    /// </summary>
    public string? PotentialProjects { get; init; }

    // ===== Pursuit-outcome / provenance columns (migration 267) =====
    //
    // Written when a pursuit closes (outcome) or at birth (provenance). The
    // store's UpdateAsync deliberately excludes ExternalSource/Key from its
    // SET list — provenance is immutable after insert (fix F2).

    /// <summary>How the pursuit resolved: Won/Lost/NoBid/Withdrawn. Null while open.</summary>
    public WonLostOutcome? WonLostOutcome { get; init; }

    /// <summary>One-line human reason captured at close ("lost on fee", "went design-build").</summary>
    public string? OutcomeReason { get; init; }

    /// <summary>FK to CanonicalOrg(Id) — who beat us, when known. Lost only.</summary>
    public long? LostToCanonicalOrgId { get; init; }

    /// <summary>Deltek project WBS1 the win became, once linked. Won only.</summary>
    public string? WonProjectWbs1 { get; init; }

    /// <summary>Optional next-action tickler (no UI consumer yet — see plan anti-feature 4).</summary>
    public DateTimeOffset? NextActionDueUtc { get; init; }

    public string? NextActionNote { get; init; }

    /// <summary>Birth provenance: 'Grab.&lt;feed&gt;' for claimed opportunities,
    /// 'Deltek.CustomProposal' for the migration-268 win backfill. Immutable.</summary>
    public string? ExternalSource { get; init; }

    /// <summary>Unique key within <see cref="ExternalSource"/> (OpportunityKey for
    /// grabs; Deltek proposal key for the backfill). Immutable.</summary>
    public string? ExternalSourceKey { get; init; }
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
