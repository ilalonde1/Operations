#nullable enable
using Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;
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
            // at 00:00 once that one ports.
            var ctrKey = new JobKey(ConcreteTestReportsRunner.Name);
            q.AddJob<ConcreteTestReportsJob>(opts => opts.WithIdentity(ctrKey));
            q.AddTrigger(t => t
                .ForJob(ctrKey)
                .WithIdentity(ConcreteTestReportsRunner.Name + "-trigger")
                .WithCronSchedule("0 30 0 1 * ?", c => c.InTimeZone(TimeZoneInfo.Local)));
        });

        services.AddQuartzHostedService(opt =>
        {
            opt.WaitForJobsToComplete = true;
        });

        return services;
    }
}
