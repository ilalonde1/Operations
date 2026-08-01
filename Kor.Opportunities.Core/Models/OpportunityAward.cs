#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// One awarded contract. Mirrors opportunities.OpportunityAwards. Distinct
/// from Opportunity (which represents pursuits KOR may bid on); Awards are
/// historical records used for competitive intelligence (who won, at what
/// price, in what geography). May or may not link to a canonical Opportunity
/// row depending on whether KOR was tracking that RFP at the time.
/// </summary>
public sealed record OpportunityAward
{
    public long Id { get; init; }

    /// <summary>Source-issued opportunity / notice id (BC Bid Opp ID). Stable key
    /// across re-ingests of the same award. UNIQUE per OpportunitySourceId.</summary>
    public string ExternalReference { get; init; } = "";

    public Guid OpportunitySourceId { get; init; }

    /// <summary>The RFP title / description.</summary>
    public string Title { get; init; } = "";

    /// <summary>Solicitation type as labeled by the source (e.g. "Invitation to Tender").</summary>
    public string? SolicitationType { get; init; }

    /// <summary>The agency that issued the original RFP.</summary>
    public string AwardingOrganization { get; init; } = "";

    /// <summary>The winning vendor.</summary>
    public string AwardedToOrganization { get; init; } = "";

    /// <summary>Awarded contract value, source-currency.</summary>
    public decimal? ContractValue { get; init; }

    public string ContractCurrency { get; init; } = "CAD";

    public DateTimeOffset? AwardedAtUtc { get; init; }

    public string? IssuingLocation { get; init; }

    public string? SupplierAddress { get; init; }

    public string? ContactEmail { get; init; }

    public string? ContractNumber { get; init; }

    /// <summary>Deep link back to the source's award detail page.</summary>
    public string SourceUrl { get; init; } = "";

    /// <summary>Verbatim row payload (HTML or JSON). Preserved so future AI
    /// features have a corpus and we can backfill fields without re-scrape.</summary>
    public string? RawJson { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The IngestionRun that first ingested this award.</summary>
    public Guid? IngestionRunId { get; init; }

    public byte[] RowVersion { get; init; } = Array.Empty<byte>();
}
