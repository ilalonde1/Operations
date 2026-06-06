#nullable enable
namespace Kor.Opportunities.Worker.Options;

public sealed class BdProjectResearchExecutorOptions
{
    public bool Enabled { get; set; } = false;

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-4-6";

    public int MaxProjectsPerRun { get; set; } = 3;

    public int MaxOutputTokens { get; set; } = 32000;

    public long DailyOutputTokenBudget { get; set; } = 200_000;

    /// <summary>
    /// MPI Stage values eligible for auto-refresh. Default = active-pipeline stages.
    /// </summary>
    public string[] EligibleStages { get; set; } = new[]
    {
        "Construction Pursuit", "Pursuit", "Capital Plan",
    };

    public int StalenessDays { get; set; } = 60;

    public string OutputDir { get; set; } =
        @"C:\ProgramData\KorOperations\Research\projects-outputs";

    public string PromptTemplatesDir { get; set; } =
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "ResearchPrompts");
}
