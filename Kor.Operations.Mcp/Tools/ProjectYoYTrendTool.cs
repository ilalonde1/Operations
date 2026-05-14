#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.PMTools;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Portfolio year-over-year trend. Wraps ProjectAnalyticsService rows +
/// YearTrendService aggregation (same source as the WPF Historical
/// Analytics YoY Trend tab).
/// </summary>
[McpServerToolType]
public sealed class ProjectYoYTrendTool
{
    private readonly ProjectAnalyticsService _projects;
    private readonly AuditLogger _audit;
    private readonly ILogger<ProjectYoYTrendTool> _logger;

    public ProjectYoYTrendTool(ProjectAnalyticsService projects, AuditLogger audit, ILogger<ProjectYoYTrendTool> logger)
    {
        _projects = projects;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_project_yoy_trend")]
    [Description(
        "Get KOR-canonical year-over-year portfolio aggregates grouped by project OpenYear. Wraps " +
        "ProjectAnalyticsService rows + YearTrendService aggregation (same source as the WPF Historical " +
        "Analytics YoY Trend tab). Returns Fee, AvgFee, AvgFeePerHr, AvgNetFeePerHr, WeightedEngPct, " +
        "WeightedBillablePct, AvgSubPct, WeightedOverheadRatio, and TotalArOutstanding per year. " +
        "Firmwide billable% by year is not surfaced (would require FirmUtilizationStats which the MCP layer " +
        "does not currently load). Use this for portfolio direction, year-over-year revenue, productivity " +
        "drift, and similar firm-trend questions.")]
    public async Task<string> GetProjectYoYTrendAsync(
        [Description("Maximum number of years to return, most recent first. Default 10, max 20.")] int? maxYears,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var n = Math.Clamp(maxYears ?? 10, 1, 20);
        try
        {
            var projectRows = await Task.Run(() => _projects.LoadProjectRowsSync(cancellationToken), cancellationToken).ConfigureAwait(false);
            var years = YearTrendService.Build(projectRows, firmUtilization: null);

            var payload = new
            {
                yearCount = years.Count,
                rows = years.Take(n).Select(r => new
                {
                    year = r.Year,
                    projectCount = r.ProjectCount,
                    totalFee = r.TotalFee,
                    avgFee = r.AvgFee,
                    avgFeePerHr = r.AvgFeePerHr,
                    avgNetFeePerHr = r.AvgNetFeePerHr,
                    weightedEngPct = r.WeightedEngPct,
                    weightedBillablePct = r.WeightedBillablePct,
                    avgSubPct = r.AvgSubPct,
                    weightedOverheadRatio = r.WeightedOverheadRatio,
                    totalArOutstanding = r.TotalArOutstanding,
                }),
                methodology =
                    "Canonical KOR YoY aggregation per YearTrendService. Groups projects by OpenYear. " +
                    "TotalFee = sum(Fee + HourlyRevenue) per year. AvgFeePerHr = TotalFee / (EngHrs + DraftHrs). " +
                    "AvgNetFeePerHr deducts SubCost before dividing. WeightedEngPct, WeightedBillablePct, " +
                    "AvgSubPct, and WeightedOverheadRatio are hours- or fee-weighted not row-averaged. Same " +
                    "definitions as the WPF YoY Trend tab.",
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
            _logger.LogWarning(ex, "get_project_yoy_trend failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_project_yoy_trend",
                InputJson: $"{{\"maxYears\":{n}}}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
