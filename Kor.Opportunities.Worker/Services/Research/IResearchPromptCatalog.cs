#nullable enable
namespace Kor.Opportunities.Worker.Services.Research;

public interface IResearchPromptCatalog
{
    /// <summary>
    /// Resolve the system + user prompt pair for a given org kind / provider.
    /// Returns null if no template is on file (caller should skip the org).
    /// </summary>
    ResearchPromptPair? Resolve(string orgKind, string providerName, string orgDisplayName);
}

public sealed record ResearchPromptPair(string SystemPrompt, string UserPrompt);
