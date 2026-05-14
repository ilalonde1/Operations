#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.Mcp.Options;
using Kor.Operations.PMTools;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical employee productivity ranking. Wraps Business
/// Historical Analytics services + EmployeePerformanceService, so rows and
/// scores match the WPF Historical Analytics Employee Summary tab.
/// </summary>
[McpServerToolType]
public sealed class EmployeePerformanceTool
{
    private readonly ProjectAnalyticsService _projects;
    private readonly EmployeeAnalyticsService _employees;
    private readonly IOptions<McpOptions> _options;
    private readonly AuditLogger _audit;
    private readonly ILogger<EmployeePerformanceTool> _logger;

    public EmployeePerformanceTool(
        ProjectAnalyticsService projects,
        EmployeeAnalyticsService employees,
        IOptions<McpOptions> options,
        AuditLogger audit,
        ILogger<EmployeePerformanceTool> logger)
    {
        _projects = projects;
        _employees = employees;
        _options = options;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_employee_performance")]
    [Description(
        "Get KOR-canonical per-employee productivity ranking from Historical Analytics. Wraps " +
        "ProjectAnalyticsService + EmployeeAnalyticsService + EmployeePerformanceService (same row construction and scoring " +
        "as the WPF Employee Summary tab). ProductivityScore = BillableRateScore*0.30 + EfficiencyScore*0.40 + " +
        "ProjectHealthScore*0.30. Use this for employee performance, productivity ranking, fee-per-hour, peer comparison, " +
        "consistency, project health, and who is the most productive employee.")]
    public async Task<string> GetEmployeePerformanceAsync(
        [Description("How many employee rows to return. Default 10, max 50.")] int? topN,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var n = Math.Clamp(topN ?? 10, 1, 50);
        try
        {
            var projectTask = Task.Run(() => _projects.LoadProjectRowsSync(cancellationToken), cancellationToken);
            var hoursTask = Task.Run(() => _employees.LoadEmployeeProjectHoursSync(cancellationToken), cancellationToken);
            await Task.WhenAll(projectTask, hoursTask).ConfigureAwait(false);

            var excluded = _options.Value.EmployeeSummaryExcludedIds;
            var rows = EmployeePerformanceService.Build(projectTask.Result, hoursTask.Result, excluded);

            var payload = new
            {
                employeeCount = rows.Count,
                rows = rows
                    .OrderByDescending(r => r.ProductivityScore)
                    .Take(n)
                    .Select(r => new
                    {
                        employeeName = r.EmployeeName,
                        employeeId = r.EmployeeId,
                        projectCount = r.ProjectCount,
                        totalEngHrs = r.TotalEngHrs,
                        totalDraftHrs = r.TotalDraftHrs,
                        totalBillableHrs = r.TotalBillableHrs,
                        totalAllHrs = r.TotalAllHrs,
                        attributedFee = r.AttributedFee,
                        feePerHr = r.FeePerHr,
                        primaryRole = r.PrimaryRole,
                        primaryConstructionType = r.PrimaryConstructionType,
                        billablePct = r.BillablePct,
                        hireDate = r.HireDate?.ToString("yyyy-MM-dd"),
                        tenureYears = r.TenureYears,
                        billableRateScore = r.BillableRateScore,
                        efficiencyScore = r.EfficiencyScore,
                        projectHealthScore = r.ProjectHealthScore,
                        productivityScore = r.ProductivityScore,
                        productivityGrade = r.ProductivityGrade,
                        consistencyScore = r.ConsistencyScore,
                        peerGroupMedianFeePerHr = r.PeerGroupMedianFeePerHr,
                        vsPeerPct = r.VsPeerPct,
                        peerCount = r.PeerCount,
                    }),
                methodology =
                    "Canonical KOR employee performance per EmployeePerformanceService. Builds employee rows from project-level historical analytics " +
                    "and employee tkDetail hours, then scores ProductivityScore = BillableRateScore*0.30 + EfficiencyScore*0.40 + ProjectHealthScore*0.30. " +
                    "BillableRateScore = TotalBillableHrs/TotalAllHrs*100; EfficiencyScore = percentile rank of FeePerHr; " +
                    "ProjectHealthScore = % of hours on projects not over budget. ConsistencyScore = coefficient of variation of hours per project. " +
                    "Peer comparison uses the same PrimaryConstructionType peer group and VsPeerPct = FeePerHr / peer median * 100 when at least 2 peers exist.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("get_employee_performance", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_employee_performance failed.");
            return ToolErrorEnvelope.FromException("get_employee_performance", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_employee_performance",
                InputJson: $"{{\"topN\":{n}}}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

}
