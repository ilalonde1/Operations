using System.Diagnostics;

namespace Kor.Operations.Mcp.Alerts;

public sealed class AlertRunner
{
    private readonly IEnumerable<IAlertRule> _rules;
    private readonly AlertRepository _repo;
    private readonly ILogger<AlertRunner> _logger;

    public AlertRunner(IEnumerable<IAlertRule> rules, AlertRepository repo, ILogger<AlertRunner> logger)
    {
        _rules = rules;
        _repo = repo;
        _logger = logger;
    }

    public async Task RunAllAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var rulesRun = 0;
        var inserted = 0;
        var deduped = 0;

        foreach (var rule in _rules)
        {
            IReadOnlyList<RichAlert> alerts;
            try
            {
                alerts = await rule.RunAsync(ct).ConfigureAwait(false);
                rulesRun++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alert rule {RuleId} failed.", rule.RuleId);
                continue;
            }

            try
            {
                var ruleInserted = 0;
                var ruleDeduped = 0;
                foreach (var alert in alerts)
                {
                    if (await _repo.HasRecentAsync(alert.RuleId, alert.Subject, 7, ct).ConfigureAwait(false))
                    {
                        deduped++;
                        ruleDeduped++;
                        continue;
                    }

                    await _repo.InsertAsync(alert, ct).ConfigureAwait(false);
                    inserted++;
                    ruleInserted++;
                }

                _logger.LogInformation(
                    "Alert rule {RuleId} inserted {Inserted} alerts, deduped {Deduped}.",
                    rule.RuleId,
                    ruleInserted,
                    ruleDeduped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alert rule {RuleId} failed while persisting alerts.", rule.RuleId);
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "Alert run complete: {RulesRun} rules run, {Inserted} alerts inserted, {Deduped} deduped in {Seconds:N1}s.",
            rulesRun,
            inserted,
            deduped,
            sw.Elapsed.TotalSeconds);
    }
}
