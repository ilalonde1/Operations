#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.PMTools;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Firmwide over-budget watchlist. Wraps ProjectAnalyticsService rows and
/// applies the canonical OverBudgetFactor threshold (same definition that
/// drives DeliveryHealthScore in get_pm_performance / get_dm_performance).
/// </summary>
[McpServerToolType]
public sealed class AtRiskProjectsTool
{
    private readonly ProjectAnalyticsService _projects;
    private readonly AuditLogger _audit;
    private readonly ILogger<AtRiskProjectsTool> _logger;

    public AtRiskProjectsTool(ProjectAnalyticsService projects, AuditLogger audit, ILogger<AtRiskProjectsTool> logger)
    {
        _projects = projects;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_at_risk_projects")]
    [Description(
        "Get the firmwide list of over-budget projects (engineering hours have exceeded " +
        "EstEngBudget * OverBudgetFactor). Wraps ProjectAnalyticsService rows. OverBudgetFactor lives in " +
        "Kor.Operations.Business.AnalyticsThresholds (1.35 today). Use this for at-risk / over-budget / " +
        "watchlist questions, and for the per-PM drilldown when a PM's DeliveryHealthScore looks low.")]
    public async Task<string> GetAtRiskProjectsAsync(
        [Description("How many rows to return, ordered by overage ratio descending. Default 25, max 100.")] int? topN,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var n = Math.Clamp(topN ?? 25, 1, 100);
        try
        {
            var rows = await Task.Run(() => _projects.LoadProjectRowsSync(cancellationToken), cancellationToken).ConfigureAwait(false);
            var threshold = AnalyticsThresholds.OverBudgetFactor;

            var atRisk = rows
                .Where(r => r.EstEngBudget > 0 && r.EngHrs > r.EstEngBudget * threshold)
                .OrderByDescending(r => r.EngHrs / r.EstEngBudget)
                .ToList();

            var payload = new
            {
                atRiskCount = atRisk.Count,
                threshold,
                rows = atRisk.Take(n).Select(r => new
                {
                    wbs1 = r.Wbs1,
                    name = r.Name,
                    pm = r.Pm,
                    draftingManager = r.DraftingManager,
                    clientId = r.ClientId,
                    phase = r.Phase,
                    status = r.Status,
                    totalFee = r.TotalFee,
                    feeBilled = r.FeeBilled,
                    percentBilled = r.PercentBilled,
                    engHrs = r.EngHrs,
                    estEngBudget = r.EstEngBudget,
                    overageRatio = r.EstEngBudget > 0 ? r.EngHrs / r.EstEngBudget : 0,
                    overageHours = r.EngHrs - r.EstEngBudget,
                    ar90Plus = r.Ar90Plus,
                    marginPct = r.MarginPct,
                }),
                methodology =
                    "Canonical KOR at-risk filter per AnalyticsThresholds.OverBudgetFactor. A project is at risk " +
                    "when EngHrs > EstEngBudget * OverBudgetFactor (1.35 today). EstEngBudget comes from the " +
                    "target-rate estimator unless a peer-budget estimator (>=3 peers) overrides it. Same threshold " +
                    "drives DeliveryHealthScore in get_pm_performance / get_dm_performance, and the WPF Historical " +
                    "Analytics OVER-BUDGET PROJECTS panel.",
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
            _logger.LogWarning(ex, "get_at_risk_projects failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_at_risk_projects",
                InputJson: $"{{\"topN\":{n}}}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        ToolErrorEnvelope.Build("get_at_risk_projects", message, errorClass: "Unknown", recoverable: true, durationMs: 0);
}
