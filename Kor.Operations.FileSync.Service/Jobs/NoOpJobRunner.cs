#nullable enable
using Kor.Operations.FileSync.Service.ControlPlane;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.FileSync.Service.Jobs;

// Fallback runner returned by JobRunnerRegistry for any job seeded in
// FileSync.Jobs that has no real runner wired up yet (still No-op for
// ConcreteTestReports / Move* / Rename* / Watcher until those steps land).
internal sealed class NoOpJobRunner : IJobRunner
{
    private readonly ILogger<NoOpJobRunner> _logger;

    public NoOpJobRunner(ILogger<NoOpJobRunner> logger)
    {
        _logger = logger;
    }

    public string JobName => "__noop__";

    public Task<JobRunResult> RunAsync(JobConfig config, string triggerSource, string? args, CancellationToken ct)
    {
        var summary = $"No-op runner: job '{config.JobName}' is registered in the control plane but no executor is wired yet (mode={config.Mode}, source={triggerSource}).";
        _logger.LogInformation("{Summary}", summary);
        return Task.FromResult(new JobRunResult(true, summary));
    }
}
