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

    // Source of truth lives in appsettings.json. In-code default is empty
    // so config Bind() can REPLACE the list rather than APPEND to it
    // (.NET array binding extends on overlap when the in-code default is
    // non-empty — saw 28 entries on 2026-06-06 with env-var overrides).
    public string[] EligibleStages { get; set; } = System.Array.Empty<string>();

    public int StalenessDays { get; set; } = 60;

    public string OutputDir { get; set; } =
        @"C:\ProgramData\KorOperations\Research\projects-outputs";

    public string PromptTemplatesDir { get; set; } =
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "ResearchPrompts");
}
