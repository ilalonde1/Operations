#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.PMTools;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical PM performance ranking. Wraps the Business
/// Historical Analytics project loader + PmPerformanceService, so rows and
/// scores match the WPF Historical Analytics PM tab by construction.
/// </summary>
[McpServerToolType]
public sealed class PmPerformanceTool
{
    private readonly ProjectAnalyticsService _projects;
    private readonly AuditLogger _audit;
    private readonly ILogger<PmPerformanceTool> _logger;

    public PmPerformanceTool(ProjectAnalyticsService projects, AuditLogger audit, ILogger<PmPerformanceTool> logger)
    {
        _projects = projects;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_pm_performance")]
    [Description(
        "Get KOR-canonical per-PM performance ranking from Historical Analytics. Wraps " +
        "ProjectAnalyticsService + PmPerformanceService (same row construction and scoring as the WPF PM tab). " +
        "PerformanceScore = DeliveryHealthScore*0.30 + EstimationAccuracyScore*0.30 + " +
        "RevenueEfficiencyScore*0.20 + ArManagementScore*0.20. Use this for PM ranking, PM performance, " +
        "delivery health, estimation accuracy, PM revenue efficiency, AR management, and who is the top PM.")]
    public async Task<string> GetPmPerformanceAsync(
        [Description("How many PM rows to return. Default 10, max 50.")] int? topN,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var n = Math.Clamp(topN ?? 10, 1, 50);
        try
        {
            var projectRows = await Task.Run(() => _projects.LoadProjectRowsSync(cancellationToken), cancellationToken).ConfigureAwait(false);
            var rows = PmPerformanceService.Build(projectRows);

            var payload = new
            {
                groupCount = rows.Count,
                rows = rows
                    .OrderByDescending(r => r.PerformanceScore)
                    .Take(n)
                    .Select(r => new
                    {
                        pm = r.Pm,
                        projectCount = r.ProjectCount,
                        totalFee = r.TotalFee,
                        totalFeeBilled = r.TotalFeeBilled,
                        totalEngHrs = r.TotalEngHrs,
                        totalDraftHrs = r.TotalDraftHrs,
                        totalSubCost = r.TotalSubCost,
                        avgEngDelta = r.AvgEngDelta,
                        avgDraftDelta = r.AvgDraftDelta,
                        deliveryHealthScore = r.DeliveryHealthScore,
                        estimationAccuracyScore = r.EstimationAccuracyScore,
                        revenueEfficiencyScore = r.RevenueEfficiencyScore,
                        arManagementScore = r.ArManagementScore,
                        performanceScore = r.PerformanceScore,
                        performanceGrade = r.PerformanceGrade,
                        uniqueClients = r.UniqueClients,
                        repeatClients = r.RepeatClients,
                        avgMonthsToFirstBill = r.AvgMonthsToFirstBill,
                        pctBilledWithin6Months = r.PctBilledWithin6Months,
                    }),
                methodology =
                    "Canonical KOR PM performance per PmPerformanceService. Groups visible firmwide historical projects by PM, " +
                    "then scores with PerformanceScore = DeliveryHealthScore*0.30 + EstimationAccuracyScore*0.30 + " +
                    "RevenueEfficiencyScore*0.20 + ArManagementScore*0.20. DeliveryHealthScore = % of projects not over budget; " +
                    "EstimationAccuracyScore = inverted percentile of |AvgEngDelta|; RevenueEfficiencyScore = percentile of AvgFeePerHr; " +
                    "ArManagementScore = 100*(1-Ar90Plus/ArTotal), clamped 0-100 by source calculation.",
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
            _logger.LogWarning(ex, "get_pm_performance failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_pm_performance",
                InputJson: $"{{\"topN\":{n}}}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
