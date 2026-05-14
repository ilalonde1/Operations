#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical 30-day utilization. Wraps UtilizationService
/// (same code path as the WPF Utilization tile + Staff Util window):
/// firmwide billable% with the canonical billable predicate (LaborCode NOT
/// IN (70 Admin, 80 NonBillable), overhead WBS1 prefixes excluded) plus
/// per-project drilldown sorted by utilizationPct desc.
/// </summary>
[McpServerToolType]
public sealed class UtilizationTool
{
    private readonly UtilizationService _svc;
    private readonly AuditLogger _audit;
    private readonly ILogger<UtilizationTool> _logger;

    public UtilizationTool(UtilizationService svc, AuditLogger audit, ILogger<UtilizationTool> logger)
    {
        _svc = svc;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_utilization")]
    [Description(
        "Get KOR-canonical 30-day firmwide utilization: billable %, billable hours, total hours, " +
        "and per-project rows sorted by utilizationPct desc. Wraps UtilizationService (same code path " +
        "as the WPF Utilization tile and Staff Util window). ALWAYS use this for 'how utilized are we', " +
        "'utilization', 'billable %', 'who is over/under-utilized' questions instead of querying " +
        "tkDetail directly - the canonical version applies the LaborCode + overhead-WBS1 billable " +
        "predicate that the rest of the financials surface uses.")]
    public async Task<string> GetUtilizationAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var result = await _svc.LoadAsync(cancellationToken).ConfigureAwait(false);

            var payload = new
            {
                pct = result.Pct,
                billableHours = result.BillableHours,
                totalHours = result.TotalHours,
                nonBillableHours = Math.Max(0.0, result.TotalHours - result.BillableHours),
                windowDays = 30,
                topProjects = result.ProjectRows.Take(50).Select(r => new
                {
                    wbs1 = r.Wbs1,
                    billableHours = r.BillableHours,
                    totalHours = r.TotalHours,
                    nonBillableHours = r.NonBillableHours,
                    utilizationPct = r.UtilizationPct,
                }),
                methodology =
                    "Canonical KOR utilization per UtilizationService. " +
                    "Window: last 30 days from tkDetail. Billable = hours where LaborCode NOT IN " +
                    "(70 Admin, 80 NonBillable) AND WBS1 NOT LIKE '[A-Z]%' / '9[A-Z]%' / '99%' " +
                    "(overhead prefixes). Denominator = ALL hours (PTO + holiday + admin in denominator, " +
                    "so firmwide utilization is intrinsically capped well below 100%). Employee must be " +
                    "active (EMCompany.Status = 'A') and the timesheet line not Rejected " +
                    "(LineItemApprovalStatus <> 'R'). Project rows aggregate the same predicate by WBS1.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("get_utilization", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_utilization failed.");
            return ToolErrorEnvelope.FromException("get_utilization", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_utilization",
                InputJson: "{}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

}
