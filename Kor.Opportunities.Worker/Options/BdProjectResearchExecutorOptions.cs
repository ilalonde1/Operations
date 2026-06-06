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
    /// MPI Stage values eligible for auto-refresh. Calibrated against
    /// production data on 2026-06-06 — the original speculative list
    /// ("Construction Pursuit / Pursuit / Capital Plan") matched zero
    /// rows, leaving the project executor a silent no-op. These 14
    /// values cover ~78% of active MPI rows (top 15 by count, less
    /// Under-Construction). The long tail of free-text Sonnet-emitted
    /// stages doesn't auto-refresh, but those are 1-of-a-kind anyway.
    /// </summary>
    public string[] EligibleStages { get; set; } = new[]
    {
        "Planned",
        "Design",
        "planning",
        "Concept",
        "Announced-Funded",
        "Approved",
        "funding-approved",
        "Procurement",
        "Approved-Funded",
        "Pre-design",
        "capital-approved",
        "Design-RFP-Open",
        "Years 4-5 priority",
        "Announced",
    };

    public int StalenessDays { get; set; } = 60;

    public string OutputDir { get; set; } =
        @"C:\ProgramData\KorOperations\Research\projects-outputs";

    public string PromptTemplatesDir { get; set; } =
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "ResearchPrompts");
}
