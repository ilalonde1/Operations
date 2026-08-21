#nullable enable
using Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;
using Kor.Operations.FileSync.Service.Jobs.KorMapSync;
using Kor.Operations.FileSync.Service.Jobs.MoveReportsToEor;
using Kor.Operations.FileSync.Service.Jobs.MoveReportsToToSend;
using Kor.Operations.FileSync.Service.Jobs.RenameReportsUploads;
using Kor.Operations.FileSync.Service.Jobs.WeeklyPmDeadlines;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Kor.Operations.FileSync.Service.Scheduling;

internal static class QuartzInstaller
{
    public static IServiceCollection AddFileSyncScheduling(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            // Misfire policy on every cron trigger:
            //   WithMisfireHandlingInstructionFireAndProceed = if the scheduler
            //   was down at fire-time (host reboot, deploy), fire ONCE on
            //   recovery instead of dropping the firing silently. Without this,
            //   a Mon 05:00 reboot quietly skips the WeeklyPmDeadlines email.

            // Cadences live in FileSyncSchedulingCatalog so the coverage test
            // can prove every real IJobRunner is cron-scheduled or explicitly
            // exempt. The DB row remains the source of truth for Enabled/Mode.
            // KorMapSync is daily @ 02:15 local: public website feed, off-hours,
            // and clear of the midnight/monthly report jobs.
            foreach (var schedule in FileSyncSchedulingCatalog.QuartzSchedules)
            {
                AddScheduledJob(q, schedule);
            }
        });

        services.AddQuartzHostedService(opt =>
        {
            // false: the Windows SCM gives us ~30s on stop. With
            // WaitForJobsToComplete=true, a long EOR/CTR batch outlives the
            // grace window and gets force-killed mid-Graph-write, which can
            // leave half-moved files. With false, Quartz signals shutdown and
            // returns; in-flight jobs observe the cancellation token from
            // JobDispatcher and finalize cleanly via JobRuns/JobTriggers
            // terminal-state writes (those use CancellationToken.None on purpose).
            opt.WaitForJobsToComplete = false;
        });

        return services;
    }

    private static void AddScheduledJob(IServiceCollectionQuartzConfigurator q, QuartzScheduledJob schedule)
    {
        if (schedule.JobType == typeof(WeeklyPmDeadlinesJob))
        {
            AddScheduledJob<WeeklyPmDeadlinesJob>(q, schedule);
        }
        else if (schedule.JobType == typeof(ConcreteTestReportsJob))
        {
            AddScheduledJob<ConcreteTestReportsJob>(q, schedule);
        }
        else if (schedule.JobType == typeof(MoveReportsToEorJob))
        {
            AddScheduledJob<MoveReportsToEorJob>(q, schedule);
        }
        else if (schedule.JobType == typeof(RenameReportsUploadsJob))
        {
            AddScheduledJob<RenameReportsUploadsJob>(q, schedule);
        }
        else if (schedule.JobType == typeof(MoveReportsToToSendJob))
        {
            AddScheduledJob<MoveReportsToToSendJob>(q, schedule);
        }
        else if (schedule.JobType == typeof(KorMapSyncJob))
        {
            AddScheduledJob<KorMapSyncJob>(q, schedule);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported FileSync Quartz job type '{schedule.JobType.FullName}'.");
        }
    }

    private static void AddScheduledJob<TJob>(IServiceCollectionQuartzConfigurator q, QuartzScheduledJob schedule)
        where TJob : IJob
    {
        var key = new JobKey(schedule.RunnerName);
        q.AddJob<TJob>(opts => opts.WithIdentity(key));
        q.AddTrigger(t => t
            .ForJob(key)
            .WithIdentity(schedule.TriggerName)
            .WithCronSchedule(schedule.CronExpression, c => c
                .InTimeZone(TimeZoneInfo.Local)
                .WithMisfireHandlingInstructionFireAndProceed()));
    }
}
