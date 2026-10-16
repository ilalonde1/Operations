#nullable enable
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service.Store;
using Quartz;

namespace Kor.Operations.NetworkOps.Service.Jobs;

/// <summary>
/// Whether each scheduled job is keeping up (GET /api/jobs; the console's status line; networkops_jobs for Claude). The
/// heartbeat proves the service is alive; it beat on while jobs failed, and NetworkOps.JobRuns -- written by every run
/// since 001 -- was read by nothing, so a sweep failing for a day showed only as readings quietly getting older
/// (audit 2026-10-16, tier 2).
/// <para>COVERS: every job in <see cref="SchedulingCatalog"/>: its latest run failed (Failing), is still running past
/// <see cref="StuckAfter"/> (Stuck), or its next scheduled start came and went with no run (Late, from its own cron);
/// otherwise Ok / Running / NeverRan. DOES NOT: judge on-demand work that is not a scheduled job (a single "check now",
/// a fix run -- those report on their own trigger or action), or a run that "succeeded" while doing nothing useful (a
/// sweep that probed 0 of 60 is Ok here; its summary says 0). Same-class fault it would miss: a job whose cron in the
/// catalog is itself wrong (never fires) looks NeverRan, not Late, until it has run once.</para>
/// </summary>
internal sealed class JobHealth(NetworkOpsStore store, TimeProvider clock)
{
    /// <summary>Longer than any job takes (the fleet sweep ~70 s, the update scan a few minutes): past this, it hangs.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(30);

    /// <summary>How long after its scheduled start a run may begin before it counts as missed (Quartz fires within seconds).</summary>
    public static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(10);

    private static readonly DateTime ProcessStartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public async Task<IReadOnlyList<JobStatusRow>> StatusAsync(CancellationToken ct)
    {
        var runs = await store.LatestJobRunsAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        return SchedulingCatalog.All.Select(j => Judge(j, runs.GetValueOrDefault(j.Name), now, ProcessStartedUtc, TimeZoneInfo.Local)).ToList();
    }

    /// <summary>One job's state from its latest run. Pure, so every state is tested without a database or a clock.</summary>
    internal static JobStatusRow Judge(ScheduledJob job, NetworkOpsStore.JobRunState? last, DateTime nowUtc, DateTime serviceStartedUtc, TimeZoneInfo zone)
    {
        // The next start the schedule promised after the last run (or after the service started, when it has never run).
        var cron = new CronExpression(job.Cron) { TimeZone = zone };
        var from = last?.StartedUtc ?? serviceStartedUtc;
        var due = cron.GetNextValidTimeAfter(new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Utc)))?.UtcDateTime;
        var missed = due is { } d && nowUtc - d > LateAfter;

        string state, text;
        if (last is null)
            (state, text) = missed ? ("Late", $"has never run; it was due {Ago(due, nowUtc)}") : ("NeverRan", "has not run since the service started");
        else if (last.Status == "Running")
            (state, text) = nowUtc - last.StartedUtc > StuckAfter
                ? ("Stuck", $"started {Ago(last.StartedUtc, nowUtc)} and has not finished: the next run waits behind it")
                : ("Running", $"running, started {Ago(last.StartedUtc, nowUtc)}");
        else if (missed)
            (state, text) = ("Late", $"did not run when due {Ago(due, nowUtc)}; last ran {Ago(last.StartedUtc, nowUtc)}");
        else if (last.Status != "Success")
            (state, text) = ("Failing", $"failed {Ago(last.FinishedUtc ?? last.StartedUtc, nowUtc)}" +
                                       (last.LastSuccessUtc is { } ok ? $"; last worked {Ago(ok, nowUtc)}" : "; it has not worked since its records began") +
                                       (last.Result is { Length: > 0 } why ? $": {FirstLine(why)}" : ""));
        else
            (state, text) = ("Ok", $"ran {Ago(last.FinishedUtc ?? last.StartedUtc, nowUtc)}");

        return new JobStatusRow(job.Name, job.Why, state, text, last?.StartedUtc, last?.FinishedUtc, last?.LastSuccessUtc, last?.Result);
    }

    private static string Ago(DateTime? t, DateTime nowUtc) => CommandCenterView.Ago(t, nowUtc);

    private static string FirstLine(string s)
    {
        var line = s.Split('\n', 2)[0].Trim();
        return line.Length <= 200 ? line : line[..199] + "…";
    }
}
