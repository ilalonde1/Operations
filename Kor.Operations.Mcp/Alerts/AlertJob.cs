using Kor.Operations.Mcp.Brief;
using Quartz;

namespace Kor.Operations.Mcp.Alerts;

[DisallowConcurrentExecution]
public sealed class AlertJob : IJob
{
    private readonly AlertRunner _runner;
    private readonly CooBriefGenerator _briefGen;
    private readonly ILogger<AlertJob> _logger;

    public AlertJob(
        AlertRunner runner,
        CooBriefGenerator briefGen,
        ILogger<AlertJob> logger)
    {
        _runner = runner;
        _briefGen = briefGen;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext ctx)
    {
        _logger.LogInformation("Scheduled alert job starting.");
        var ct = ctx.CancellationToken;
        await _runner.RunAllAsync(ct).ConfigureAwait(false);

        var today = DateTime.Today;
        var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekOf = today.AddDays(-daysSinceMonday);

        try
        {
            await _briefGen.GenerateForWeekAsync(weekOf, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CooBriefGenerator failed.");
        }
    }
}
