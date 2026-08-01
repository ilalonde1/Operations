#nullable enable
using System;

namespace Kor.Opportunities.Core.Models;

/// <summary>
/// One ingestion hit — a single row from a CSV, an RSS item, an email, etc.
/// Many observations may link to one canonical <see cref="Opportunity"/>
/// (auto-merged or manual). A null <see cref="OpportunityId"/> means the
/// observation hasn't been linked yet.
///
/// <see cref="HashSha256"/> is the de-dup key; computed from
/// <c>(Title|Buyer|Location|Url)</c> uppercased and pipe-separated. The unique
/// index <c>UX_Opp_Obs_HashSha256</c> enforces this server-side.
/// </summary>
public sealed record OpportunityObservation
{
    public long Id { get; init; }

    public long? OpportunityId { get; init; }

    public Guid OpportunitySourceId { get; init; }

    public string Title { get; init; } = "";

    public string Buyer { get; init; } = "";

    public string? Location { get; init; }

    public string Url { get; init; } = "";

    public string? Description { get; init; }

    /// <summary>Source-specific raw payload (CSV row JSON, RSS item, etc.).
    /// Preserved verbatim so future AI features have a corpus.</summary>
    public string? RawJson { get; init; }

    public DateTimeOffset? PostedDateUtc { get; init; }

    public DateTimeOffset IngestedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>SHA-256 of the deduplication key. Always 32 bytes.</summary>
    public byte[] HashSha256 { get; init; } = Array.Empty<byte>();

    /// <summary>Soft-delete flag. Inactive observations are hidden from the
    /// UI but kept on disk so the dedup index remains effective.</summary>
    public bool IsActive { get; init; } = true;
}
