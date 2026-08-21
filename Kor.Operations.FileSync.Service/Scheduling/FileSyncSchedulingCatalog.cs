#nullable enable
using Kor.Operations.FileSync.Service.Jobs;
using Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;
using Kor.Operations.FileSync.Service.Jobs.KorMapSync;
using Kor.Operations.FileSync.Service.Jobs.MoveReportsToEor;
using Kor.Operations.FileSync.Service.Jobs.MoveReportsToToSend;
using Kor.Operations.FileSync.Service.Jobs.RenameReportsUploads;
using Kor.Operations.FileSync.Service.Jobs.Watcher;
using Kor.Operations.FileSync.Service.Jobs.WeeklyPmDeadlines;

namespace Kor.Operations.FileSync.Service.Scheduling;

internal sealed record QuartzScheduledJob(
    Type RunnerType,
    string RunnerName,
    Type JobType,
    string TriggerName,
    string CronExpression);

internal sealed record JobRunnerSchedulingExemption(Type RunnerType, string Reason);

internal static class FileSyncSchedulingCatalog
{
    internal static IReadOnlyList<QuartzScheduledJob> QuartzSchedules { get; } =
    [
        new(
            typeof(WeeklyPmDeadlinesRunner),
            WeeklyPmDeadlinesRunner.Name,
            typeof(WeeklyPmDeadlinesJob),
            WeeklyPmDeadlinesRunner.Name + "-trigger",
            "0 0 5 ? * MON"),
        new(
            typeof(ConcreteTestReportsRunner),
            ConcreteTestReportsRunner.Name,
            typeof(ConcreteTestReportsJob),
            ConcreteTestReportsRunner.Name + "-trigger",
            "0 30 0 1 * ?"),
        new(
            typeof(MoveReportsToEorRunner),
            MoveReportsToEorRunner.Name,
            typeof(MoveReportsToEorJob),
            MoveReportsToEorRunner.Name + "-trigger",
            "0 0 0 1 * ?"),
        new(
            typeof(RenameReportsUploadsRunner),
            RenameReportsUploadsRunner.Name,
            typeof(RenameReportsUploadsJob),
            RenameReportsUploadsRunner.Name + "-trigger",
            "0 30 23 * * ?"),
        new(
            typeof(MoveReportsToToSendRunner),
            MoveReportsToToSendRunner.Name,
            typeof(MoveReportsToToSendJob),
            MoveReportsToToSendRunner.Name + "-trigger",
            "0 0 8 5 * ?"),
        new(
            typeof(KorMapSyncRunner),
            KorMapSyncRunner.Name,
            typeof(KorMapSyncJob),
            KorMapSyncRunner.Name + "-trigger",
            "0 15 2 * * ?"),
    ];

    internal static IReadOnlyList<JobRunnerSchedulingExemption> SchedulingExemptions { get; } =
    [
        new(typeof(WatcherSyncRunner), "Driven by WatcherHostedService; event-driven, not cron."),
        new(typeof(NoOpJobRunner), "Stub fallback for unknown control-plane jobs; not a runnable scheduled job."),
    ];
}
