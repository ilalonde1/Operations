#nullable enable
using Kor.Operations.NetworkOps.Service.Jobs;
using Kor.Operations.NetworkOps.Service.Store;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Whether each scheduled job is keeping up (Service/Jobs/JobHealth, GET /api/jobs). The heartbeat beat on while jobs failed,
// and JobRuns was written by every run and read by nothing (audit 2026-10-16, tier 2).
//
// WHAT IT COVERS: Ok, Running, Failing (with when it last worked), Stuck (running past StuckAfter), Late (its own cron's next
// start came and went), NeverRan, judged in the catalog's time zone; every catalog job gets a row.
// WHAT IT DOES NOT: the SQL that reads the latest run per job, or the console/CLI/MCP rendering -- the deployed /api/jobs
// answer is read after the deploy. A SAME-CLASS FAULT IT WOULD NOT CATCH: a run that "succeeds" having done nothing (a sweep
// that probed 0 PCs) is Ok here.
public sealed class JobHealthTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Mountain Standard Time");
    // Friday 16 Oct 2026, 10:12 local (MDT = UTC-6).
    private static readonly DateTime Now = new(2026, 10, 16, 16, 12, 0, DateTimeKind.Utc);
    private static readonly ScheduledJob Rack = new(typeof(object), "RackSweep", "0 0/5 * * * ?", "every 5 minutes");
    private static readonly ScheduledJob Health = new(typeof(object), "HealthSweep", "0 30 7-18 ? * MON-FRI", "hourly, weekdays");

    private static NetworkOpsStore.JobRunState Run(DateTime started, string status, DateTime? lastOk = null, string? result = null)
        => new("x", started, status == "Running" ? null : started.AddSeconds(30), status, result, lastOk);

    private static string State(ScheduledJob job, NetworkOpsStore.JobRunState? last) => JobHealth.Judge(job, last, Now, Now.AddDays(-3), Zone).State;

    [Fact]
    public void Each_state_is_judged_from_the_latest_run_and_the_jobs_own_schedule()
    {
        Assert.Equal("Ok", State(Rack, Run(Now.AddMinutes(-2), "Success")));
        Assert.Equal("Running", State(Rack, Run(Now.AddMinutes(-1), "Running")));
        Assert.Equal("Stuck", State(Rack, Run(Now.AddMinutes(-45), "Running")));
        Assert.Equal("Late", State(Rack, Run(Now.AddMinutes(-40), "Success")));          // due 35 min ago, never ran
        Assert.Equal("Ok", State(Health, Run(Now.AddMinutes(-42), "Success")));           // 09:30 ran; 10:30 not due yet
        Assert.Equal("Late", State(Health, Run(Now.AddHours(-3), "Success")));            // 08:30 and 09:30 both missed
        Assert.Equal("Late", State(Rack, null));                                           // never ran in 3 days of uptime
        Assert.Equal("NeverRan", JobHealth.Judge(Rack, null, Now, Now.AddMinutes(-1), Zone).State);   // just started

        var failing = JobHealth.Judge(Rack, Run(Now.AddMinutes(-2), "Failed", lastOk: Now.AddHours(-5), result: "could not read MeshCentral\nstack..."), Now, Now.AddDays(-3), Zone);
        Assert.Equal("Failing", failing.State);
        Assert.True(failing.NeedsAttention);
        Assert.Contains("last worked 5 h ago", failing.Text);
        Assert.Contains("could not read MeshCentral", failing.Text);
        Assert.DoesNotContain("stack", failing.Text);
    }

    [Fact]
    public void A_weekend_is_not_late_for_a_weekday_job()
    {
        var sunday = new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Utc);
        var fridayLastRun = new DateTime(2026, 10, 17, 0, 30, 0, DateTimeKind.Utc);   // Friday 18:30 local
        Assert.Equal("Ok", JobHealth.Judge(Health, Run(fridayLastRun, "Success"), sunday, sunday.AddDays(-5), Zone).State);
    }

    [Fact]
    public void Every_scheduled_job_has_a_cron_the_judge_can_read()
        => Assert.All(SchedulingCatalog.All, j => JobHealth.Judge(j, null, Now, Now, Zone));
}
