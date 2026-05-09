using System.Text.Json.Serialization;

namespace Kor.Operations.Mcp.Collections;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CollectionsCaseStatus
{
    Open,
    Lien,
    Foreclosure,
    WriteOff,
    Resolved,
}

public sealed record CollectionsCaseRow(
    long Id,
    string ClientID,
    string Status,
    DateTime OpenedAt,
    string OpenedBy,
    DateTime LastUpdatedAt,
    string LastUpdatedBy,
    DateTime? ResolvedAt,
    decimal? LegalAmount,
    string? Notes);

public sealed record OpenCollectionsCaseRequest(
    string ClientID,
    CollectionsCaseStatus Status,
    decimal? LegalAmount,
    string? Notes);

public sealed record UpdateCollectionsCaseRequest(
    CollectionsCaseStatus Status,
    decimal? LegalAmount,
    string? Notes);
