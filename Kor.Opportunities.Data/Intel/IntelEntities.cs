#nullable enable
namespace Kor.Opportunities.Data.Intel;

public enum IntelConfidence
{
    High,
    Medium,
    Low,
}

public sealed record IntelPersonDraft(
    string DisplayName,
    string? Email,
    string? Phone,
    string? LinkedinUrl,
    string? Notes,
    IntelConfidence Confidence);

public sealed record IntelPersonAffiliationDraft(
    string PersonDisplayName,
    long CanonicalOrgId,
    string? Title,
    string? Department,
    bool IsCurrent,
    string? StartDateApprox,
    string? EndDateApprox,
    string? Notes,
    IntelConfidence Confidence);

public sealed record IntelSignalDraft(
    long CanonicalOrgId,
    string SignalType,
    string Subject,
    string? Detail,
    string? OccurredAtApprox,
    string? SourceUrl,
    IntelConfidence Confidence);

public sealed record IntelActionDraft(
    long CanonicalOrgId,
    string ActionType,
    string Recommendation,
    string? TargetPersonName,
    string? TimingNotes,
    IntelConfidence Confidence);

public sealed record IntelWorkDraft(
    long CanonicalOrgId,
    string ProjectName,
    string? Role,
    string? YearApprox,
    decimal? EstimatedValueCad,
    string? EstimatedValueText,
    string? Notes,
    IntelConfidence Confidence);

public sealed record IntelRiskDraft(
    long CanonicalOrgId,
    string RiskType,
    string Description,
    string? MitigationNotes,
    IntelConfidence Confidence);

public sealed record IntelNarrativeDraft(
    long CanonicalOrgId,
    string NarrativeType,
    string ParagraphText,
    IntelConfidence Confidence);

public sealed record ExtractedIntel(
    IReadOnlyList<IntelPersonDraft> People,
    IReadOnlyList<IntelPersonAffiliationDraft> Affiliations,
    IReadOnlyList<IntelSignalDraft> Signals,
    IReadOnlyList<IntelActionDraft> Actions,
    IReadOnlyList<IntelWorkDraft> Works,
    IReadOnlyList<IntelRiskDraft> Risks,
    IReadOnlyList<IntelNarrativeDraft> Narratives)
{
    public static ExtractedIntel Empty { get; } = new(
        Array.Empty<IntelPersonDraft>(),
        Array.Empty<IntelPersonAffiliationDraft>(),
        Array.Empty<IntelSignalDraft>(),
        Array.Empty<IntelActionDraft>(),
        Array.Empty<IntelWorkDraft>(),
        Array.Empty<IntelRiskDraft>(),
        Array.Empty<IntelNarrativeDraft>());
}
