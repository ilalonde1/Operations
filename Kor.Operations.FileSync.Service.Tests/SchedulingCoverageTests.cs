#nullable enable
using Kor.Operations.FileSync.Service.Jobs;
using Kor.Operations.FileSync.Service.Jobs.KorMapSync;
using Kor.Operations.FileSync.Service.Scheduling;
using Quartz;
using Xunit;

namespace Kor.Operations.FileSync.Service.Tests;

public sealed class SchedulingCoverageTests
{
    [Fact]
    public void Every_job_runner_is_scheduled_or_explicitly_exempt()
    {
        var runnerTypes = typeof(IJobRunner).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(IJobRunner).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        var scheduled = FileSyncSchedulingCatalog.QuartzSchedules
            .Select(s => s.RunnerType)
            .ToHashSet();

        var exempt = FileSyncSchedulingCatalog.SchedulingExemptions
            .Select(e => e.RunnerType)
            .ToHashSet();

        var missing = runnerTypes
            .Where(t => !scheduled.Contains(t) && !exempt.Contains(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(missing);
        Assert.All(FileSyncSchedulingCatalog.SchedulingExemptions, e => Assert.False(string.IsNullOrWhiteSpace(e.Reason)));
    }

    [Fact]
    public void Scheduled_entries_are_real_quartz_jobs_and_include_kor_map_sync()
    {
        var schedules = FileSyncSchedulingCatalog.QuartzSchedules;

        Assert.Contains(schedules, s =>
            s.RunnerType == typeof(KorMapSyncRunner)
            && s.RunnerName == KorMapSyncRunner.Name
            && s.CronExpression == "0 15 2 * * ?");

        Assert.All(schedules, s =>
        {
            Assert.True(typeof(IJobRunner).IsAssignableFrom(s.RunnerType), s.RunnerType.FullName);
            Assert.True(typeof(IJob).IsAssignableFrom(s.JobType), s.JobType.FullName);
            Assert.EndsWith("-trigger", s.TriggerName, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(s.CronExpression));
        });
    }
}
