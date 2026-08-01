namespace Kor.Operations.Mcp.Brief;

public enum BriefSection
{
    FinancialHealth,
    PortfolioHealth,
    ClientStrategy,
    BdMarket,
    OperationsTalent,
    WatchItems,
}

public sealed record RichBriefSection(
    BriefSection Section,
    string Headline,
    string Body,
    string? Recommendation,
    int InputTokens,
    int OutputTokens,
    int ToolCalls);

public sealed record BriefRow(
    long Id,
    DateTime GeneratedAt,
    DateTime WeekOf,
    string Section,
    string Headline,
    string Body,
    string? Recommendation,
    int InputTokens,
    int OutputTokens,
    int ToolCalls,
    DateTime? AcknowledgedAt,
    string? AcknowledgedBy);
