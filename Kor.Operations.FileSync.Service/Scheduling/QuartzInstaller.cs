#nullable enable
using Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;
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
            // WeeklyPmDeadlines: Mondays @ 05:00 (matches PS1 Scheduled Task and the
            // FileSync.Jobs seed row). Cron is static here on purpose -- the DB row
            // is the source of truth for Enabled/Mode, and a per-job runner reads
            // its own knobs at fire time. Adjusting cadence still requires a
            // service restart; that's fine for now.
            var weeklyKey = new JobKey(WeeklyPmDeadlinesRunner.Name);
            q.AddJob<WeeklyPmDeadlinesJob>(opts => opts.WithIdentity(weeklyKey));
            q.AddTrigger(t => t
                .ForJob(weeklyKey)
                .WithIdentity(WeeklyPmDeadlinesRunner.Name + "-trigger")
                .WithCronSchedule("0 0 5 ? * MON", c => c.InTimeZone(TimeZoneInfo.Local)));

            // ConcreteTestReports: 1st of month @ 00:30 PT (matches PS1 task and
            // FileSync.Jobs seed). Half-hour offset leaves room for MoveReportsToEor
            // at 00:00.
            var ctrKey = new JobKey(ConcreteTestReportsRunner.Name);
            q.AddJob<ConcreteTestReportsJob>(opts => opts.WithIdentity(ctrKey));
            q.AddTrigger(t => t
                .ForJob(ctrKey)
                .WithIdentity(ConcreteTestReportsRunner.Name + "-trigger")
                .WithCronSchedule("0 30 0 1 * ?", c => c.InTimeZone(TimeZoneInfo.Local)));

            // MoveReportsToEor: 1st of month @ 00:00 PT (matches PS1 Scheduled
            // Task and FileSync.Jobs seed). Runs ahead of ConcreteTestReports so
            // freshly distributed Reports/ folders are clear before CTR scans.
            var eorKey = new JobKey(MoveReportsToEorRunner.Name);
            q.AddJob<MoveReportsToEorJob>(opts => opts.WithIdentity(eorKey));
            q.AddTrigger(t => t
                .ForJob(eorKey)
                .WithIdentity(MoveReportsToEorRunner.Name + "-trigger")
                .WithCronSchedule("0 0 0 1 * ?", c => c.InTimeZone(TimeZoneInfo.Local)));

            // RenameReportsUploads: every night @ 23:30 PT (matches PS1
            // Scheduled Task on KOR-APP01). Mode is read from FileSync.Jobs
            // at fire time, so flipping Live/Shadow doesn't require a redeploy.
            var renameKey = new JobKey(RenameReportsUploadsRunner.Name);
            q.AddJob<RenameReportsUploadsJob>(opts => opts.WithIdentity(renameKey));
            q.AddTrigger(t => t
                .ForJob(renameKey)
                .WithIdentity(RenameReportsUploadsRunner.Name + "-trigger")
                .WithCronSchedule("0 30 23 * * ?", c => c.InTimeZone(TimeZoneInfo.Local)));

            // MoveReportsToToSend: 5th of every month @ 08:00 PT (matches the
            // "Move Reports From EOR To Server" Scheduled Task on KOR-APP01).
            // EORs ack their monthly reports any time in the first few days
            // of the month; firing on the 5th gives them a buffer.
            var toSendKey = new JobKey(MoveReportsToToSendRunner.Name);
            q.AddJob<MoveReportsToToSendJob>(opts => opts.WithIdentity(toSendKey));
            q.AddTrigger(t => t
                .ForJob(toSendKey)
                .WithIdentity(MoveReportsToToSendRunner.Name + "-trigger")
                .WithCronSchedule("0 0 8 5 * ?", c => c.InTimeZone(TimeZoneInfo.Local)));
        });

        services.AddQuartzHostedService(opt =>
        {
            opt.WaitForJobsToComplete = true;
        });

        return services;
    }
}
