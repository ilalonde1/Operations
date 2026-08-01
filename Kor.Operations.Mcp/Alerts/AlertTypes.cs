namespace Kor.Operations.Mcp.Alerts;

public enum AlertSection
{
    CashAndFinancials,
    ProjectHealth,
    Clients,
    BusinessDevelopment,
}

public enum AlertSeverity
{
    Low,
    Medium,
    High,
}

public sealed record RichAlert(
    string RuleId,
    AlertSection Section,
    AlertSeverity Severity,
    string Title,
    string Body,
    string? Subject = null);

public sealed record AlertRow(
    long Id,
    DateTime GeneratedAt,
    string RuleId,
    string Section,
    string Severity,
    string? Subject,
    string Title,
    string Body,
    DateTime? AcknowledgedAt,
    string? AcknowledgedBy);
