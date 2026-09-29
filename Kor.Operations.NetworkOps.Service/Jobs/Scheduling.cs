#nullable enable
using Kor.Operations.NetworkOps.Service.Alerting;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Kor.Operations.NetworkOps.Service.Jobs;

/// <param name="Cron">Quartz cron, in the server's local time.</param>
internal sealed record ScheduledJob(Type JobType, string Name, string Cron, string Why);

// The ONE place every schedule lives. A job that is not in this list does not run, and a test
// (SchedulingCatalogTests) fails if any INetworkOpsJob is missing from it -- the Opportunities
// worker lost jobs to schedules that lived elsewhere and nobody could see them.
internal static class SchedulingCatalog
{
    public static readonly IReadOnlyList<ScheduledJob> All =
    [
        new(typeof(FleetCensusJob), FleetCensusJob.JobName, "0 5 7-18 ? * MON-FRI",
            "hourly in business hours; PCs are off at night, so a night census only proves they are off"),
        new(typeof(HealthSweepJob), HealthSweepJob.JobName, "0 30 10,14 ? * MON-FRI",
            "mid-morning and mid-afternoon, when the fleet is on and users have been working long enough to show faults"),
        new(typeof(MaintenanceJob), MaintenanceJob.JobName, "0 0 3 * * ?",
            "nightly observation purge; nothing else runs then"),
    ];
}

// The single path every run takes: record the start, run, record the result, alert on failure.
// Terminal writes use CancellationToken.None so a run caught by shutdown still ends as a row.
internal sealed class JobDispatcher(NetworkOpsStore store, IDigestSender alerts, ILogger<JobDispatcher> log)
{
    public static readonly string Version = typeof(JobDispatcher).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task RunAsync(INetworkOpsJob job, CancellationToken ct)
    {
        long runId;
        try { runId = await store.StartRunAsync(job.Name, Environment.MachineName, Version, ct); }
        catch (Exception ex) { log.LogError(ex, "Could not record the start of {Job}; running it unrecorded is worse than skipping it", job.Name); return; }

        try
        {
            var summary = await job.RunAsync(ct);
            await store.FinishRunAsync(runId, true, summary, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await store.FinishRunAsync(runId, false, "cancelled by shutdown", null);
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "{Job} run {RunId} failed", job.Name, runId);
            await store.FinishRunAsync(runId, false, ex.Message, ex.ToString());
            using var cap = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await alerts.SendAlertAsync($"[NetworkOps] '{job.Name}' run {runId} FAILED on {Environment.MachineName}", ex.ToString(), cap.Token);
        }
    }
}

[DisallowConcurrentExecution]
internal sealed class QuartzShim<TJob>(TJob job, JobDispatcher dispatcher) : IJob where TJob : INetworkOpsJob
{
    public Task Execute(IJobExecutionContext context) => dispatcher.RunAsync(job, context.CancellationToken);
}

internal static class SchedulingSetup
{
    public static IServiceCollection AddNetworkOpsScheduling(this IServiceCollection services)
    {
        foreach (var s in SchedulingCatalog.All) services.AddSingleton(s.JobType);
        services.AddQuartz(q =>
        {
            foreach (var s in SchedulingCatalog.All)
            {
                var shim = typeof(QuartzShim<>).MakeGenericType(s.JobType);
                var key = new JobKey(s.Name);
                q.AddJob(shim, key, j => j.WithDescription(s.Why));
                q.AddTrigger(t => t.ForJob(key).WithIdentity(s.Name + "-trigger")
                    .WithCronSchedule(s.Cron, c => c.InTimeZone(TimeZoneInfo.Local).WithMisfireHandlingInstructionDoNothing()));
            }
        });
        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = false);
        foreach (var s in SchedulingCatalog.All) services.AddTransient(typeof(QuartzShim<>).MakeGenericType(s.JobType));
        return services;
    }
}
