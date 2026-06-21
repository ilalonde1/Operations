#nullable enable
namespace Kor.Opportunities.Worker.Services.Research;

public interface IAwardProgramResearchPromptCatalog
{
    ResearchPromptPair? Resolve();
}
