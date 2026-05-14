#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.PMTools;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical per-employee utilization over the last 12 weeks.
/// Wraps EmployeeAnalyticsService weekly utilization rows from the same
/// Historical Analytics source used by WPF.
/// </summary>
[McpServerToolType]
public sealed class EmployeeUtilizationTool
{
    private readonly EmployeeAnalyticsService _employees;
    private readonly AuditLogger _audit;
    private readonly ILogger<EmployeeUtilizationTool> _logger;

    public EmployeeUtilizationTool(EmployeeAnalyticsService employees, AuditLogger audit, ILogger<EmployeeUtilizationTool> logger)
    {
        _employees = employees;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_employee_utilization")]
    [Description(
        "Get KOR-canonical per-employee billable utilization over the last 12 weeks from Historical Analytics. " +
        "BillablePct = billable hours / total hours, where billable excludes LaborCode 70 Admin and 80 NonBillable " +
        "and overhead WBS prefixes. Use this for employee utilization, who is most/least utilized, last-12-week " +
        "billable percentage, and staff utilization by employee.")]
    public async Task<string> GetEmployeeUtilizationAsync(
        [Description("How many employee rows to return. Default 25, max 100.")] int? topN,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var n = Math.Clamp(topN ?? 25, 1, 100);
        try
        {
            var weekly = await Task.Run(() => _employees.LoadEmployeeWeeklyUtilizationSync(cancellationToken), cancellationToken).ConfigureAwait(false);
            var totalBillable = weekly.Sum(r => r.BillableHrs);
            var totalHours = weekly.Sum(r => r.TotalHrs);

            var rows = weekly
                .GroupBy(r => new { r.EmployeeId, r.EmployeeName })
                .Select(g =>
                {
                    var billable = g.Sum(r => r.BillableHrs);
                    var total = g.Sum(r => r.TotalHrs);
                    return new
                    {
                        employeeName = g.Key.EmployeeName,
                        employeeId = g.Key.EmployeeId,
                        last12wkBillableHrs = billable,
                        last12wkTotalHrs = total,
                        last12wkBillablePct = total > 0 ? billable / total : 0.0,
                        weeklySeries = g
                            .OrderBy(r => r.WeekStart)
                            .Select(r => new
                            {
                                weekStart = r.WeekStart.ToString("yyyy-MM-dd"),
                                billablePct = r.TotalHrs > 0 ? r.BillableHrs / r.TotalHrs : 0.0,
                            }),
                    };
                })
                .OrderByDescending(r => r.last12wkBillablePct)
                .Take(n)
                .ToList();

            var payload = new
            {
                employeeCount = weekly.Select(r => r.EmployeeId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                firmwideBillablePct = totalHours > 0 ? totalBillable / totalHours : 0.0,
                rows,
                methodology =
                    "Canonical KOR employee utilization per EmployeeAnalyticsService. Uses the last 12 weeks of tkDetail grouped by employee. " +
                    "BillablePct = billable hours / total hours; billable excludes LaborCode 70 Admin and 80 NonBillable and overhead WBS prefixes, " +
                    "matching the Staff Utilization definition. FirmwideBillablePct is weighted by hours, not an average of employee percentages.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("get_employee_utilization", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_employee_utilization failed.");
            return ToolErrorEnvelope.FromException("get_employee_utilization", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_employee_utilization",
                InputJson: $"{{\"topN\":{n}}}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

}
