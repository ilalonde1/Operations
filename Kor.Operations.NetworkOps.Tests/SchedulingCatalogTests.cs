#nullable enable
using System.Reflection;
using Kor.Operations.NetworkOps.Service.Jobs;
using Quartz;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Every job the service can run is in the one scheduling catalog, with a valid cron and a stated
// reason -- a job defined but never scheduled is invisible, the failure the Opportunities worker
// had. WHAT IT DOES NOT COVER: that the cron times are the right ones for this office.
public sealed class SchedulingCatalogTests
{
    [Fact]
    public void Every_job_is_scheduled_exactly_once_with_a_valid_cron_and_a_reason()
    {
        var jobs = typeof(JobDispatcher).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(INetworkOpsJob).IsAssignableFrom(t)).ToList();
        Assert.NotEmpty(jobs);
        foreach (var j in jobs)
            Assert.Single(SchedulingCatalog.All, s => s.JobType == j);

        foreach (var s in SchedulingCatalog.All)
        {
            Assert.True(CronExpression.IsValidExpression(s.Cron), $"{s.Name}: invalid cron '{s.Cron}'");
            Assert.False(string.IsNullOrWhiteSpace(s.Why), $"{s.Name}: no reason given");
            var nameField = s.JobType.GetField("JobName", BindingFlags.Public | BindingFlags.Static);
            Assert.Equal(nameField?.GetValue(null), s.Name);
        }
        Assert.Equal(SchedulingCatalog.All.Count, SchedulingCatalog.All.Select(s => s.Name).Distinct().Count());
    }
}
