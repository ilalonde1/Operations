#nullable enable
using System;
using System.Collections.Generic;
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

    // Three-tier health for the Command Center chip. The thresholds mirror
    // the TriggerPoller's HeartbeatStaleCutoff (5 min) so "Down" here means
    // "the dead-host claim recovery sweep would now reclaim this host's
    // triggers."
    public string HealthStatus
    {
        get
        {
            var s = SinceLastHeartbeat.TotalMinutes;
            if (s < 2) return "Live";
            if (s < 5) return "Stale";
            return "Down";
        }
    }

    public string HealthLabel => $"{HealthStatus} ({HumanizeShort(SinceLastHeartbeat)})";

    private static string HumanizeShort(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{(int)t.TotalSeconds}s" :
        t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes}m" :
        t.TotalHours < 24 ? $"{(int)t.TotalHours}h" :
        $"{(int)t.TotalDays}d";
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
                if (!nextUtc.HasValue) return null;
                // GetNextOccurrence returns Kind=Utc; the DateTimeOffset
                // (DateTime, offset) ctor rejects Utc-kind with a non-zero
                // offset. Build a UTC DateTimeOffset first, then shift to
                // local for display.
                return new DateTimeOffset(nextUtc.Value).ToLocalTime();
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

    // Row-state signals for the Command Center DataGrid. The window's
    // auto-refresh timer (15s) re-fetches Jobs from the control plane, so
    // these recompute every tick without needing INotifyPropertyChanged --
    // the bound row instance itself is replaced.
    public bool IsImminent
    {
        get
        {
            var t = UntilNextFire;
            return t.HasValue && t.Value > TimeSpan.Zero && t.Value <= TimeSpan.FromMinutes(5);
        }
    }

    public bool IsRunning => string.Equals(LastRunStatus, "Running", StringComparison.Ordinal);

    // Watcher manual fire requires an encoded WatcherArgs payload; firing it
    // with null args (the path the simple "Manual fire" button takes) is a
    // guaranteed bad-args failure. Hide the button for Watcher rows so we
    // don't pollute the failure bell with avoidable runs.
    public bool IsManualFireable => !string.Equals(JobName, "Watcher", StringComparison.Ordinal);

    // Last N runs, oldest -> newest, used by the Trend column to draw a
    // per-job mini-sparkline. Each dot binds to the JobRunRow itself so
    // clicks can drop the log viewer onto that exact run's time window.
    // Stitched in the VM after both Jobs and RecentRuns have been fetched.
    public IReadOnlyList<JobRunRow> RecentRunsTrend { get; init; } = Array.Empty<JobRunRow>();
}

public sealed class JobRunRow
{
    public long RunId { get; init; }

    public string JobName { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string TriggerSource { get; init; } = string.Empty;

    public string? TriggeredBy { get; init; }

    public string? Summary { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorStack { get; init; }

    public string HostName { get; init; } = string.Empty;

    public string? ServiceVersion { get; init; }

    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : (TimeSpan?)null;

    // Pre-formatted tooltip for the per-job trend dots in the Jobs grid.
    // Built here so the XAML doesn't need a multi-binding or extra converter.
    public string TrendTooltip
    {
        get
        {
            var dur = Duration.HasValue
                ? (Duration.Value.TotalSeconds < 60
                    ? $"{Duration.Value.TotalSeconds:0.0}s"
                    : $"{(int)Duration.Value.TotalMinutes}m {Duration.Value.Seconds}s")
                : "running";
            return $"{Status} · {StartedAt.LocalDateTime:HH:mm:ss} · {dur} (click for logs)";
        }
    }
}

public sealed class PendingTriggerRow
{
    public long TriggerId { get; init; }

    public string JobName { get; init; } = string.Empty;

    public DateTimeOffset RequestedAt { get; init; }

    public string RequestedBy { get; init; } = string.Empty;

    public string? Args { get; init; }

    public TimeSpan WaitingFor => DateTimeOffset.Now - RequestedAt;
}

// One projected upcoming cron fire. Built in the VM by walking each enabled
// job's CronExpression with Cronos. Display-only; nothing on the service
// side reads this back.
public sealed class UpcomingFireRow
{
    public string JobName { get; init; } = string.Empty;

    public DateTimeOffset FireAt { get; init; }

    public TimeSpan TimeUntil => FireAt - DateTimeOffset.Now;
}

public sealed class JobKnobRow
{
    public string KnobName { get; set; } = string.Empty;

    public string? KnobValue { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string UpdatedBy { get; set; } = string.Empty;
}
