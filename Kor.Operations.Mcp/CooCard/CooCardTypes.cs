namespace Kor.Operations.Mcp.CooCard;

/// <summary>
/// One ranked action item on the weekly COO Card. The Card itself is the
/// ordered set of these (1..N, typically 5). Generated from the latest
/// CooBrief sections + active Alerts via a strategic-synthesis pass on
/// the same /ask gateway used by the brief generator.
/// </summary>
public sealed record RichCooCardItem(
    int Rank,
    string Severity,
    string Headline,
    string Body,
    string Recommendation,
    string SourceTags);

public sealed record CooCardItemRow(
    long Id,
    DateTime GeneratedAt,
    DateTime WeekOf,
    int Rank,
    string Severity,
    string Headline,
    string Body,
    string Recommendation,
    string? SourceTags,
    DateTime? AcknowledgedAt,
    string? AcknowledgedBy);
