#nullable enable
namespace Kor.Opportunities.Worker.Services.Research;

public interface IPersonResearchPromptCatalog
{
    /// <summary>
    /// Resolve the system + user prompt pair for a given person + provider.
    /// Returns null if no template is on file (caller should skip the person).
    /// </summary>
    ResearchPromptPair? Resolve(
        string providerName,
        string personDisplayName,
        string? currentTitle,
        string? currentEmployerName,
        long intelPersonId);
}
