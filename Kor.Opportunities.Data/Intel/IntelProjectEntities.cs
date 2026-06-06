#nullable enable
namespace Kor.Opportunities.Data.Intel;

public sealed record IntelProjectDraft(
    long MajorProjectsInventoryId,
    string? Description,
    string? Schedule,
    string? Status,
    string? KorAngle,
    IntelConfidence Confidence);

public sealed record IntelProjectSignalDraft(
    long MajorProjectsInventoryId,
    string SignalType,
    string Subject,
    string? Detail,
    string? OccurredAtApprox,
    string? SourceUrl,
    IntelConfidence Confidence);

public sealed record IntelProjectActionDraft(
    long MajorProjectsInventoryId,
    string ActionType,
    string Recommendation,
    string? TargetPersonName,
    long? TargetCanonicalOrgId,
    string? TimingNotes,
    IntelConfidence Confidence);

public sealed record IntelProjectRiskDraft(
    long MajorProjectsInventoryId,
    string RiskType,
    string Description,
    string? MitigationNotes,
    IntelConfidence Confidence);

public sealed record IntelProjectKeyPersonDraft(
    long MajorProjectsInventoryId,
    string DisplayName,
    string? Title,
    string Side,
    long? CanonicalOrgId,
    IntelConfidence Confidence);

public sealed record ExtractedProjectIntel(
    IReadOnlyList<IntelProjectDraft> Projects,
    IReadOnlyList<IntelProjectSignalDraft> Signals,
    IReadOnlyList<IntelProjectActionDraft> Actions,
    IReadOnlyList<IntelProjectRiskDraft> Risks,
    IReadOnlyList<IntelProjectKeyPersonDraft> KeyPeople)
{
    public static ExtractedProjectIntel Empty { get; } = new(
        Array.Empty<IntelProjectDraft>(),
        Array.Empty<IntelProjectSignalDraft>(),
        Array.Empty<IntelProjectActionDraft>(),
        Array.Empty<IntelProjectRiskDraft>(),
        Array.Empty<IntelProjectKeyPersonDraft>());
}
