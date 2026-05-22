#nullable enable
using System;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// One row yielded by an <see cref="IOpportunityProvider"/>. Candidate-level
/// fields only — no Id, no RowVersion, no score. The ingestion service
/// turns these into <see cref="Models.OpportunityObservation"/> +
/// <see cref="Models.Opportunity"/> rows downstream.
/// </summary>
public sealed record OpportunityCandidate
{
    public string Title { get; init; } = "";

    public string Buyer { get; init; } = "";

    public string? Location { get; init; }

    public string Url { get; init; } = "";

    public string? Description { get; init; }

    public DateTimeOffset? PostedDateUtc { get; init; }

    /// <summary>
    /// Raw source payload (CSV row JSON, RSS item, etc.). Stored on the
    /// observation so future AI features have the original text.
    /// </summary>
    public string? RawJson { get; init; }

    /// <summary>Optional structured fields a provider can supply when known.
    /// Mapped onto the canonical <see cref="Models.Opportunity"/>.</summary>
    public string? ProjectCity { get; init; }

    public string? ProjectProvince { get; init; }

    public decimal? EstimatedValueCad { get; init; }

    public DateTimeOffset? SubmissionDeadlineUtc { get; init; }

    /// <summary>Stable external identifier from the source (e.g. CanadaBuys
    /// referenceNumber). Used to compose <c>Opportunity.OpportunityKey</c>;
    /// fall back to a hash if absent.</summary>
    public string? ExternalReference { get; init; }

    /// <summary>
    /// Optional internal identifier the source system uses for its own DB
    /// (e.g. BC Bid's numeric process id like "53056"). Distinct from
    /// <see cref="ExternalReference"/>, which is the human-facing solicitation
    /// number. Populated on archive pipelines that have access to it; null
    /// otherwise.
    /// </summary>
    public string? SourceInternalId { get; init; }
}
