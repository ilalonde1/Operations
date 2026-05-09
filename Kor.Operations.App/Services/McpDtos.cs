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
    int invoiceCount);
