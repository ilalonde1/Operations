using Kor.Operations.Mcp.Brief;
using Kor.Operations.Mcp.CooCard;
using Kor.Operations.Mcp.Director;
using Quartz;

namespace Kor.Operations.Mcp.Alerts;

[DisallowConcurrentExecution]
public sealed class AlertJob : IJob
{
    private readonly AlertRunner _runner;
    private readonly CooBriefGenerator _briefGen;
    private readonly CooCardGenerator _cardGen;
    private readonly DirectorBriefGenerator _directorGen;
    private readonly ILogger<AlertJob> _logger;

    public AlertJob(
        AlertRunner runner,
        CooBriefGenerator briefGen,
        CooCardGenerator cardGen,
        DirectorBriefGenerator directorGen,
        ILogger<AlertJob> logger)
    {
        _runner = runner;
        _briefGen = briefGen;
        _cardGen = cardGen;
        _directorGen = directorGen;
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

        // COO Card follows the COO brief — it consumes the just-persisted
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
        try
        {
            ct.ThrowIfCancellationRequested();
            await _directorGen.GenerateForWeekAsync(weekOf, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "DirectorBriefGenerator failed."); }
    }
}
