#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>Buyer / official-contact fields, when the source exposes them.
    /// Mapped onto <see cref="Models.Opportunity"/> BuyerContact* columns.</summary>
    public string? BuyerContactName { get; init; }

    public string? BuyerContactEmail { get; init; }

    public string? BuyerContactPhone { get; init; }

    /// <summary>Raw commodity / category signals the source carries — UNSPSC or
    /// GSIN or NAICS codes and/or their text (e.g. "81101505 - Structural
    /// engineering"). Fed to <c>DisciplineClassifier</c> to derive
    /// <see cref="Models.Opportunity.Discipline"/>. Null when unknown.</summary>
    public IReadOnlyList<string>? CommodityCodes { get; init; }

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

    /// <summary>
    /// Optional provider acknowledgement to run only after this candidate has
    /// crossed the durable persistence boundary.
    /// </summary>
    public Func<CancellationToken, Task>? OnPersistedAsync { get; init; }

    /// <summary>
    /// Optional provider acknowledgement to run when ingestion rejects this
    /// candidate before persistence (relevance-gate reject or blank title/url
    /// skip). Stateful providers (e.g. GraphEmail) use this to ack the source
    /// item so rejected candidates aren't re-fetched and re-rejected forever.
    /// Null (the default for all other providers) is a no-op.
    /// </summary>
    public Func<CancellationToken, Task>? OnRejectedAsync { get; init; }
}
