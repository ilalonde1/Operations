#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// Canonical opportunity record. One row per real-world RFP — many
/// <c>OpportunityObservations</c> may link to the same canonical
/// row (auto-merged or manual).
///
/// Mirrors <c>opportunities.Opportunities</c>. RowVersion drives optimistic
/// concurrency on every update path.
/// </summary>
public sealed record Opportunity
{
    /// <summary>IDENTITY column. 0 for unsaved rows.</summary>
    public long Id { get; init; }

    /// <summary>Stable business key (UNIQUE). For manual entries, callers compose
    /// a value such as <c>MAN-yyyyMMdd-{shortHash}</c>; ingestion sources use
    /// their own naming convention (e.g. <c>CB-{noticeId}</c>).</summary>
    public string OpportunityKey { get; init; } = "";

    public string Name { get; init; } = "";

    // Buyer / issuer
    public string BuyerName { get; init; } = "";
    public BuyerType BuyerType { get; init; } = BuyerType.Unknown;
    public string? DeltekClientId { get; init; }
    /// <summary>FK to Deltek dbo.Contacts.ContactID. Null when the buyer-side person isn't in Deltek yet.</summary>
    public string? DeltekContactId { get; init; }
    /// <summary>Free-text buyer-side contact name for prospects not yet in Deltek.</summary>
    public string? BuyerContactName { get; init; }
    /// <summary>Free-text buyer-side contact email for prospects not yet in Deltek.</summary>
    public string? BuyerContactEmail { get; init; }
    /// <summary>Free-text buyer-side contact phone for prospects not yet in Deltek.</summary>
    public string? BuyerContactPhone { get; init; }

    // Project location
    public string? ProjectAddress { get; init; }
    public string? ProjectCity { get; init; }
    public string? ProjectProvince { get; init; }
    public string? ProjectPostalCode { get; init; }
    public decimal? ProjectLatitude { get; init; }
    public decimal? ProjectLongitude { get; init; }

    // Discipline classification
    public OpportunityDiscipline Discipline { get; init; } = OpportunityDiscipline.Unknown;
    public string? ConstructionType { get; init; }
    public string? ProjectCategory { get; init; }

    // Money + timeline
    public decimal? EstimatedValue { get; init; }
    public string EstimatedValueCurrency { get; init; } = "CAD";
    public DateOnly? RfpReleaseDate { get; init; }
    public DateTimeOffset? SubmissionDeadlineUtc { get; init; }

    // Pursuit lifecycle
    public OpportunityStatus Status { get; init; } = OpportunityStatus.New;
    public DateTimeOffset IdentifiedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewingSinceUtc { get; init; }
    public DateTimeOffset? QualifiedAtUtc { get; init; }
    public DateTimeOffset? PursuingSinceUtc { get; init; }
    public DateTimeOffset? ProposalSubmittedAtUtc { get; init; }
    public DateTimeOffset? OutcomeAtUtc { get; init; }

    // Assignment
    public string? OwnerStaffId { get; init; }

    /// <summary>Comma-separated reviewer staff IDs (matches the schema's nvarchar(500) bag).</summary>
    public string? ReviewerStaffIds { get; init; }

    // Scoring (refreshed on insert/update by the scoring service in Phase 3)
    public decimal? RelevanceScore { get; init; }
    public RelevanceTier? RelevanceTier { get; init; }

    // Outcome
    public WonLostOutcome? WonLostOutcome { get; init; }
    public string? OutcomeReason { get; init; }
    public string? WonProjectWbs1 { get; init; }
    public decimal? AwardedValue { get; init; }

    // Audit
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = "";

    /// <summary>Optimistic-concurrency token. SQL Server <c>rowversion</c>; opaque 8-byte value.</summary>
    public byte[] RowVersion { get; init; } = Array.Empty<byte>();
}
