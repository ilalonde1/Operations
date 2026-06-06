#nullable enable
namespace Kor.Opportunities.Worker.Services.Research;

public interface IResearchExecutorService
{
    Task<ExecutedResearch?> ExecuteAsync(ResearchTarget target, CancellationToken ct);
}

public sealed record ResearchTarget(
    long CanonicalOrgId,
    string OrgDisplayName,
    string OrgKind,
    string ProviderName,
    string SystemPromptOverride,
    string UserPromptOverride,
    string? StructuredOutputJsonSchema = null,
    string? StructuredOutputFormatInstruction = null);

public sealed record ExecutedResearch(
    long CanonicalOrgId,
    string ProviderName,
    string ResultJson,
    long InputTokens,
    long OutputTokens,
    int ToolCallCount,
    TimeSpan Elapsed);
