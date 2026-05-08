using Quartz;

namespace Kor.Operations.Mcp.Alerts;

[DisallowConcurrentExecution]
public sealed class AlertJob : IJob
{
    private readonly AlertRunner _runner;
    private readonly ILogger<AlertJob> _logger;

    public AlertJob(AlertRunner runner, ILogger<AlertJob> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext ctx)
    {
        _logger.LogInformation("Scheduled alert job starting.");
        return _runner.RunAllAsync(ctx.CancellationToken);
    }
}
