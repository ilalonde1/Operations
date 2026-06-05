#nullable enable
namespace Kor.Opportunities.Worker.Options;

public sealed class BdResearchExecutorOptions
{
    /// <summary>Master kill switch. Defaults to false  opt-in.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Anthropic API key. Read from env var ANTHROPIC_API_KEY at runtime.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Anthropic model id (e.g. "claude-sonnet-4-6"). Default to Sonnet 4.6.</summary>
    public string Model { get; set; } = "claude-sonnet-4-6";

    /// <summary>Max orgs to research per scheduled run.</summary>
    public int MaxOrgsPerRun { get; set; } = 3;

    /// <summary>Hard cap on output tokens per single research call.</summary>
    public int MaxOutputTokens { get; set; } = 32000;

    /// <summary>Soft cap on total output tokens per scheduled run. Job stops queueing new orgs once exceeded.</summary>
    public long DailyOutputTokenBudget { get; set; } = 200_000;

    /// <summary>Restrict auto-refresh to canonical orgs whose Kind is one of these values. Default = KOR-relevant kinds only (skip noise Vendor mass).</summary>
    public string[] EligibleKinds { get; set; } = new[]
    {
        "Architect", "Buyer", "Competitor", "Developer", "GC", "KorClient", "KorStructural",
    };

    /// <summary>Days since LastSeenAtUtc above which an org is eligible for refresh.</summary>
    public int StalenessDays { get; set; } = 21;

    /// <summary>Output folder where executed-research JSON files are written. BdResearchImport picks them up from here on its existing schedule.</summary>
    public string OutputDir { get; set; } = @"C:\ProgramData\KorOperations\Research\outputs";

    /// <summary>Folder containing the PROMPT.md templates the executor loads to feed the model. Mirrors the KOR-*\PROMPT.md convention used by interactive Sonnet sessions.</summary>
    public string PromptTemplatesDir { get; set; } = System.IO.Path.Combine(System.AppContext.BaseDirectory, "ResearchPrompts");
}
