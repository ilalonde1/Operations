using Kor.Operations.Mcp.Brief;
using Kor.Operations.Mcp.CooCard;
using Quartz;

namespace Kor.Operations.Mcp.Alerts;

[DisallowConcurrentExecution]
public sealed class AlertJob : IJob
{
    private readonly AlertRunner _runner;
    private readonly CooBriefGenerator _briefGen;
    private readonly CooCardGenerator _cardGen;
    private readonly ILogger<AlertJob> _logger;

    public AlertJob(
        AlertRunner runner,
        CooBriefGenerator briefGen,
        CooCardGenerator cardGen,
        ILogger<AlertJob> logger)
    {
        _runner = runner;
        _briefGen = briefGen;
        _cardGen = cardGen;
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

        // COO Card runs LAST in the chain — it consumes the just-persisted
        // brief sections + active alerts to produce Ian's Top 5. Wrapped in
        // its own try so a Card failure can't roll back the brief or the
        // alert run.
        try
        {
            await _cardGen.GenerateForWeekAsync(weekOf, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CooCardGenerator failed.");
        }
    }
}
