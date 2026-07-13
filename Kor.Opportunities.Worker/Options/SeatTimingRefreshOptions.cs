#nullable enable
namespace Kor.Opportunities.Worker.Options;

/// <summary>
/// SeatTimingRefreshJob: re-checks the SE-seat timing (now/2026/2027+/filled) of
/// the oldest sheet-relevant plays on a small daily budget, so the attack sheet
/// never leads with a stale 'now'. Reuses the Anthropic web-search research
/// engine. Off by default — enabling spends on the Claude API.
/// </summary>
public sealed class SeatTimingRefreshOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>Max plays re-checked per run (each is a 1-4 min web-search call).</summary>
    public int MaxPerRun { get; set; } = 5;

    /// <summary>A play is re-checked once its timing is older than this. The
    /// sheet stops trusting timing at 45 days, so 30 leaves a refresh buffer.</summary>
    public int StaleDays { get; set; } = 30;

    /// <summary>Hard stop for the run's output tokens (cost guard).</summary>
    public long DailyOutputTokenBudget { get; set; } = 100_000;

    public string? CronSchedule { get; set; }
}
