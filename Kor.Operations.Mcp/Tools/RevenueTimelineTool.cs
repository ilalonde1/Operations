#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.PMTools;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Firmwide revenue timeline by period (yyyymm). Aggregates the per-WBS1
/// revenue timelines from ProjectAnalyticsService into a single firmwide
/// time series — same PRSummaryMain source as the per-project timeline on
/// the WPF row detail.
/// </summary>
[McpServerToolType]
public sealed class RevenueTimelineTool
{
    private readonly ProjectAnalyticsService _projects;
    private readonly AuditLogger _audit;
    private readonly ILogger<RevenueTimelineTool> _logger;

    public RevenueTimelineTool(ProjectAnalyticsService projects, AuditLogger audit, ILogger<RevenueTimelineTool> logger)
    {
        _projects = projects;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_revenue_timeline")]
    [Description(
        "Get KOR-canonical firmwide revenue timeline by period (yyyymm). Aggregates the per-WBS1 timeline " +
        "from ProjectAnalyticsService.LoadRevenueTimelineSync into a single firmwide series. Revenue uses " +
        "the same predicate as the WPF row detail: SUM(BilledFee else Revenue) from PRSummaryMain per " +
        "period — BilledFee wins when non-zero, falling back to Revenue (legacy projects). Also returns " +
        "Billed (raw PRSummaryMain.Billed). Use this for month-by-month revenue trend, posting-lag analysis, " +
        "or recent-vs-historical comparisons. Per-project timeline is on get_project_detail.")]
    public async Task<string> GetRevenueTimelineAsync(
        [Description("Maximum number of periods to return, most recent first. Default 24, max 60.")] int? maxPeriods,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var n = Math.Clamp(maxPeriods ?? 24, 1, 60);
        try
        {
            var byWbs = await Task.Run(() => _projects.LoadRevenueTimelineSync(cancellationToken), cancellationToken).ConfigureAwait(false);

            var firmwide = byWbs
                .SelectMany(kvp => kvp.Value)
                .GroupBy(p => p.Period, StringComparer.Ordinal)
                .Select(g => new
                {
                    period = g.Key,
                    revenue = g.Sum(p => p.Revenue),
                    billed = g.Sum(p => p.Billed),
                    projectCount = g.Count(),
                })
                .OrderByDescending(p => p.period, StringComparer.Ordinal)
                .ToList();

            var payload = new
            {
                periodCount = firmwide.Count,
                rows = firmwide.Take(n),
                methodology =
                    "Canonical KOR firmwide revenue timeline aggregated from " +
                    "ProjectAnalyticsService.LoadRevenueTimelineSync. Source: PRSummaryMain grouped by " +
                    "WBS1 + Period; revenue uses CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) " +
                    "(canonical KOR predicate — Revenue Generation is OFF, so BilledFee is primary). Billed is " +
                    "raw PRSummaryMain.Billed. Periods are yyyymm strings; sort descending = most recent first. " +
                    "Same revenue accounting as the per-project timeline returned by get_project_detail.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return JsonError(errorMessage);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_revenue_timeline failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_revenue_timeline",
                InputJson: $"{{\"maxPeriods\":{n}}}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
