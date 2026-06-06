#nullable enable
namespace Kor.Opportunities.Worker.Options;

/// <summary>
/// Options for the person-research auto-refresh executor. Mirrors
/// <see cref="BdResearchExecutorOptions"/> (orgs) and
/// <see cref="BdProjectResearchExecutorOptions"/> (projects) but with a
/// person-shaped staleness contract: candidates are IntelPerson rows whose
/// most recent IntelPerson*-side row is older than StalenessDays AND who
/// have a current affiliation we can attribute new signals/actions to.
/// </summary>
public sealed class BdPersonResearchExecutorOptions
{
    /// <summary>Master kill switch. Defaults to false — opt-in.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Anthropic API key. Read from env var ANTHROPIC_API_KEY at runtime.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Anthropic model id (e.g. "claude-sonnet-4-6"). Default Sonnet 4.6.</summary>
    public string Model { get; set; } = "claude-sonnet-4-6";

    /// <summary>Max people to refresh per scheduled run.</summary>
    public int MaxPeoplePerRun { get; set; } = 3;

    /// <summary>Hard cap on output tokens per single research call.</summary>
    public int MaxOutputTokens { get; set; } = 32000;

    /// <summary>Soft cap on total output tokens per scheduled run. Job stops queueing once exceeded.</summary>
    public long DailyOutputTokenBudget { get; set; } = 200_000;

    /// <summary>
    /// Days since the last DEDICATED PersonBrief refresh above which a
    /// person is eligible. People who've never had a PersonBrief refresh
    /// are eligible immediately. Org-side R83 refreshes do NOT count —
    /// they bump IntelPerson.LastSeenAtUtc but don't satisfy person-side
    /// refresh budget. See R91c-tune.
    /// </summary>
    public int StalenessDays { get; set; } = 90;

    /// <summary>Output folder where executed-research JSON files are archived.</summary>
    public string OutputDir { get; set; } = @"C:\ProgramData\KorOperations\Research\people-outputs";

    /// <summary>
    /// Folder containing the PROMPT.md templates the executor loads. Same
    /// convention as the org/project catalogs — <c>{root}/PersonBrief/system.md</c>
    /// and <c>{root}/PersonBrief/user.md</c>.
    /// </summary>
    public string PromptTemplatesDir { get; set; } = System.IO.Path.Combine(System.AppContext.BaseDirectory, "ResearchPrompts");
}
