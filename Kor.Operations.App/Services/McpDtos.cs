namespace Kor.Operations.Services;

internal sealed record AlertDto(
    long id,
    DateTime generatedAt,
    string ruleId,
    string section,
    string severity,
    string? subject,
    string title,
    string body,
    DateTime? acknowledgedAt,
    string? acknowledgedBy);

internal sealed record BriefDto(
    long id,
    DateTime generatedAt,
    DateTime weekOf,
    string section,
    string headline,
    string body,
    string? recommendation,
    int inputTokens,
    int outputTokens,
    int toolCalls,
    DateTime? acknowledgedAt,
    string? acknowledgedBy);

internal sealed record CollectionsCaseDto(
    long id,
    string clientID,
    string status,
    DateTime openedAt,
    string openedBy,
    DateTime lastUpdatedAt,
    string lastUpdatedBy,
    DateTime? resolvedAt,
    decimal? legalAmount,
    string? notes,
    int invoiceCount,
    DateTime? lienExpiryDate);

internal sealed record CollectionsCaseInvoiceDto(
    long id,
    long caseId,
    string wbS1,
    string invoiceNumber,
    DateTime addedAt,
    string addedBy);

internal sealed record CollectionsCaseDetailDto(
    CollectionsCaseDto header,
    IReadOnlyList<CollectionsCaseInvoiceDto> invoices);

internal sealed record ClientArInvoiceDto(
    string wbS1,
    string invoiceNumber,
    string? projectName,
    string currency,
    decimal originalAmount,
    decimal outstandingBalance,
    DateTime invoiceDate,
    int daysOutstanding,
    long? activeCaseId);

internal sealed record InvoiceRefDto(string wbS1, string invoiceNumber);

internal sealed record ActiveCaseInvoiceDto(
    long caseId,
    string clientID,
    string status,
    string wbS1,
    string invoiceNumber);

internal sealed record OpenCollectionsCaseRequestDto(
    string clientID,
    string status,
    decimal? legalAmount,
    string? notes,
    IReadOnlyList<InvoiceRefDto>? invoices,
    DateTime? lienExpiryDate);

internal sealed record UpdateCollectionsCaseRequestDto(
    string status,
    decimal? legalAmount,
    string? notes,
    IReadOnlyList<InvoiceRefDto>? invoices,
    DateTime? lienExpiryDate);
