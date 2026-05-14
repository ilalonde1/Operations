#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.PMTools;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical Drafting Manager performance ranking. Wraps the
/// Business Historical Analytics project loader + DmPerformanceService, so
/// rows and scores match the WPF Historical Analytics DM tab by construction.
/// </summary>
[McpServerToolType]
public sealed class DmPerformanceTool
{
    private readonly ProjectAnalyticsService _projects;
    private readonly AuditLogger _audit;
    private readonly ILogger<DmPerformanceTool> _logger;

    public DmPerformanceTool(ProjectAnalyticsService projects, AuditLogger audit, ILogger<DmPerformanceTool> logger)
    {
        _projects = projects;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_dm_performance")]
    [Description(
        "Get KOR-canonical per-Drafting-Manager performance ranking from Historical Analytics. Wraps " +
        "ProjectAnalyticsService + DmPerformanceService (same row construction and scoring as the WPF DM tab). " +
        "PerformanceScore = DeliveryHealthScore*0.30 + EstimationAccuracyScore*0.30 + " +
        "RevenueEfficiencyScore*0.20 + ArManagementScore*0.20. Use this for drafting manager ranking, DM performance, " +
        "delivery health, estimation accuracy, revenue efficiency, AR management, and who is the top DM.")]
    public async Task<string> GetDmPerformanceAsync(
        [Description("How many Drafting Manager rows to return. Default 10, max 50.")] int? topN,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var n = Math.Clamp(topN ?? 10, 1, 50);
        try
        {
            var projectRows = await Task.Run(() => _projects.LoadProjectRowsSync(cancellationToken), cancellationToken).ConfigureAwait(false);
            var rows = DmPerformanceService.Build(projectRows);

            var payload = new
            {
                groupCount = rows.Count,
                rows = rows
                    .OrderByDescending(r => r.PerformanceScore)
                    .Take(n)
                    .Select(r => new
                    {
                        dm = r.Pm,
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
                    "Canonical KOR Drafting Manager performance per DmPerformanceService. Groups visible firmwide historical projects by DraftingManager, " +
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
            _logger.LogWarning(ex, "get_dm_performance failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_dm_performance",
                InputJson: $"{{\"topN\":{n}}}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        ToolErrorEnvelope.Build("get_dm_performance", message, errorClass: "Unknown", recoverable: true, durationMs: 0);
}
