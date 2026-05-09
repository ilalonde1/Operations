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
    string? Notes,
    int InvoiceCount);

public sealed record InvoiceRef(string WBS1, string InvoiceNumber);

public sealed record CollectionsCaseInvoiceRow(
    long Id,
    long CaseId,
    string WBS1,
    string InvoiceNumber,
    DateTime AddedAt,
    string AddedBy);

public sealed record CollectionsCaseDetailRow(
    CollectionsCaseRow Header,
    IReadOnlyList<CollectionsCaseInvoiceRow> Invoices);

public sealed record OpenCollectionsCaseRequest(
    string ClientID,
    CollectionsCaseStatus Status,
    decimal? LegalAmount,
    string? Notes,
    IReadOnlyList<InvoiceRef>? Invoices = null);

public sealed record UpdateCollectionsCaseRequest(
    CollectionsCaseStatus Status,
    decimal? LegalAmount,
    string? Notes,
    IReadOnlyList<InvoiceRef>? Invoices = null);
