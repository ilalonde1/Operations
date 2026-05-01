#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Kor.Operations.FileSync.Service.Scheduling;

internal static class QuartzInstaller
{
    public static IServiceCollection AddFileSyncScheduling(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            // No jobs registered yet. Each migration step (Send-Weekly-PM-Deadlines first)
            // will register its job and trigger here.
        });

        services.AddQuartzHostedService(opt =>
        {
            opt.WaitForJobsToComplete = true;
        });

        return services;
    }
}
