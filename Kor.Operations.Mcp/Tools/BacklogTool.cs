#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical Backlog snapshot. Wraps BacklogService:
/// firmwide TotalFee / FeeBilled (posted + unposted overlay) / Backlog /
/// %Billed across all active projects, with project + client + PM
/// resolved server-side. TotalFee includes T&amp;M HourlyRevenue extras; the
/// Billed side includes the LedgerAR overlay covering invoices cut but
/// not yet posted to PRSummaryMain (Deltek's ~3-month close lag).
/// </summary>
[McpServerToolType]
public sealed class BacklogTool
{
    private readonly BacklogService _svc;
    private readonly AuditLogger _audit;
    private readonly ILogger<BacklogTool> _logger;

    public BacklogTool(BacklogService svc, AuditLogger audit, ILogger<BacklogTool> logger)
    {
        _svc = svc;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_backlog")]
    [Description(
        "Get KOR-canonical Backlog firmwide across all active projects: TotalFee (PR.Fee + T&M " +
        "HourlyRevenue extras), FeeBilled (posted via PRSummaryMain + unposted overlay via LedgerAR), " +
        "Backlog = TotalFee - FeeBilled, and percentBilled. Plus per-project drilldown (50 max) " +
        "with resolved projectName / clientName / pm, sorted by backlog desc. Wraps BacklogService " +
        "(same canonical formula as the WPF Financials window). Use this for 'backlog', 'remaining " +
        "fee', 'billing runway', 'how much work is unbilled' questions instead of querying PR + " +
        "PRSummaryMain directly - the canonical version includes T&M HourlyRevenue on the Fee side " +
        "and the LedgerAR unposted-billing overlay on the Billed side. Skipping either gives wrong " +
        "numbers. Surface clientName not raw clientId in narrative.")]
    public async Task<string> GetBacklogAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var result = await _svc.LoadAsync(cancellationToken).ConfigureAwait(false);

            var payload = new
            {
                activeProjectCount = result.ActiveProjectCount,
                dataLoaded = result.DataLoaded,
                firmwide = new
                {
                    totalFee = result.FirmwideTotalFee,
                    feeBilled = result.FirmwideFeeBilled,
                    unpostedFeeBilled = result.FirmwideUnpostedFeeBilled,
                    backlog = result.FirmwideBacklog,
                    percentBilled = result.FirmwidePercentBilled,
                },
                topProjects = result.TopProjects.Take(50).Select(r => new
                {
                    wbs1 = r.Wbs1,
                    projectName = r.ProjectName,
                    clientId = r.ClientId,
                    clientName = r.ClientName,
                    pm = r.Pm,
                    org = r.Org,
                    totalFee = r.TotalFee,
                    feeBilled = r.FeeBilled,
                    unpostedFeeBilled = r.UnpostedFeeBilled,
                    backlog = r.Backlog,
                    percentBilled = r.PercentBilled,
                }),
                methodology =
                    "Canonical KOR Backlog per BacklogService. Scope: all active projects " +
                    "(PR.Status='A', master rows only). TotalFee = PR.Fee + HourlyRevenue (T&M extras " +
                    "from sub-task PRSummaryMain rows where pr.Fee=0 and WBS2/WBS3 are populated). " +
                    "FeeBilled = SUM(PRSummaryMain.BilledFee else Revenue) per WBS1 (posted) + " +
                    "UnpostedFeeBilled overlay = SUM(MAX(0, LedgerAR_invoiced - PRSummaryMain_billed)) " +
                    "per (WBS1, Period) (LedgerAR filter: TransType='IN' AND Account prefix in " +
                    "{4001,4003,4210,4220,4240}). Backlog = TotalFee - FeeBilled. FX: USA-org projects " +
                    "converted to CAD-equivalent at Financials.Billed.UsdToCadRate before summing. " +
                    "PercentBilled denominator is TotalFee. Drilldown rows JOIN PR + Clendor + EMMain " +
                    "for resolved names - surface clientName, NEVER raw clientId.",
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
            _logger.LogWarning(ex, "get_backlog failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_backlog",
                InputJson: "{}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
