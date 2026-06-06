#nullable enable
namespace Kor.Opportunities.Worker.Services.Research;

public interface IProjectResearchPromptCatalog
{
    ResearchPromptPair? Resolve(
        string projectStage,
        string providerName,
        string projectName,
        string? proponentName,
        string? city,
        string? province);
}
