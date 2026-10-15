namespace Kor.Operations.Mcp.Director;

public enum DirectorSection { ThisWeek, Pursuits, Accounts, Market, Asks }
public sealed record DirectorCitation(long ClaimId, long PassageId, string Statement, string Source);
public sealed record RichDirectorSection(DirectorSection Section, string Headline, string Body, string? Recommendation,
    IReadOnlyList<DirectorCitation> Citations, int InputTokens, int OutputTokens, int ToolCalls);
public sealed record DirectorBriefRow(long Id, DateTime GeneratedAt, DateTime WeekOf, string Section, string Headline,
    string Body, string? Recommendation, int InputTokens, int OutputTokens, int ToolCalls,
    DateTime? AcknowledgedAt, string? AcknowledgedBy, string Citations);
public sealed record DirectorAnswer(string Answer, int InputTokens, int OutputTokens, int ToolCalls);
