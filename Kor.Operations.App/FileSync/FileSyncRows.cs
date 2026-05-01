#nullable enable
using Cronos;

namespace Kor.Operations.App.FileSync;

public sealed class HeartbeatRow
{
    public string HostName { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset LastHeartbeatAt { get; init; }

    public string GlobalMode { get; init; } = string.Empty;

    public string? ServiceVersion { get; init; }

    public int? WatcherGen { get; init; }

    public int JobsRegistered { get; init; }

    public TimeSpan SinceLastHeartbeat => DateTimeOffset.Now - LastHeartbeatAt;

    public bool IsStale => SinceLastHeartbeat > TimeSpan.FromMinutes(5);
}

public sealed class JobRow
{
    public string JobName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Mode { get; init; } = "Shadow";

    public string? CronExpression { get; init; }

    public bool Enabled { get; init; }

    public DateTimeOffset LastConfigChangedAt { get; init; }

    public string? Notes { get; init; }

    // A3: latest run snapshot from a LEFT JOIN against FileSync.JobRuns.
    // All three are nullable -- a freshly-seeded job has never run yet.
    public long? LastRunId { get; init; }

    public string? LastRunStatus { get; init; }

    public DateTimeOffset? LastRunStartedAt { get; init; }

    public DateTimeOffset? LastRunCompletedAt { get; init; }

    public string? LastRunSummary { get; init; }

    // A3: derived client-side from CronExpression so the UI can show
    // "next fire in 3d 14h" without round-tripping to Quartz on the server.
    // Null = manual-only (CronExpression is null) or unparseable cron.
    public DateTimeOffset? NextFireAt
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CronExpression))
                return null;
            try
            {
                // Quartz writes cron in "sec min hour day-of-month month day-of-week" form.
                // Cronos defaults to 5-field cron; pass IncludeSeconds for parity with Quartz.
                var expr = Cronos.CronExpression.Parse(CronExpression, CronFormat.IncludeSeconds);
                var nextUtc = expr.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Local);
                return nextUtc.HasValue
                    ? new DateTimeOffset(nextUtc.Value, TimeZoneInfo.Local.GetUtcOffset(nextUtc.Value))
                    : (DateTimeOffset?)null;
            }
            catch
            {
                return null;
            }
        }
    }

    public TimeSpan? SinceLastRun => LastRunStartedAt.HasValue
        ? DateTimeOffset.Now - LastRunStartedAt.Value
        : (TimeSpan?)null;

    public TimeSpan? UntilNextFire => NextFireAt.HasValue
        ? NextFireAt.Value - DateTimeOffset.Now
        : (TimeSpan?)null;
}
