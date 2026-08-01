#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical WIP (Work In Progress) snapshot. Wraps
/// WipFinancialsService (same code path as the WPF WIP tile + watchlist):
/// firmwide Earned / Overbilled / Net at the latest posted period, plus
/// per-project drilldown. Auto-detects whether Deltek Revenue Generation
/// is configured. KOR has PRSummaryMain.Unbilled populated, so the Revenue
/// Generation path is what runs in production. If Unbilled is absent, the
/// service falls back to the Revenue - Billed proxy.
/// </summary>
[McpServerToolType]
public sealed class WipTool
{
    private readonly WipFinancialsService _svc;
    private readonly AuditLogger _audit;
    private readonly ILogger<WipTool> _logger;

    public WipTool(WipFinancialsService svc, AuditLogger audit, ILogger<WipTool> logger)
    {
        _svc = svc;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_wip")]
    [Description(
        "Get KOR-canonical WIP (Work In Progress) firmwide: Earned (revenue recognized but not yet " +
        "billed), Overbilled (billed in advance of recognition), Net, the as-of posting period, plus " +
        "per-project drilldown (50 max) WITH resolved project name + client name + PM, sorted by " +
        "Overbilled desc then Earned desc. Wraps WipFinancialsService (same code path as the WPF " +
        "WIP tile). Auto-detects whether Deltek Revenue Generation is on (uses PRSummaryMain.Unbilled " +
        "directly) or off (proxies via Revenue - Billed). KOR has Revenue Generation ON for this " +
        "purpose because PRSummaryMain.Unbilled is populated. Positive Net = earned-not-invoiced; " +
        "negative Net = overbilled. Surface clientName, NEVER the raw clientId code, in narrative output.")]
    public async Task<string> GetWipAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var result = await _svc.LoadAsync(cancellationToken).ConfigureAwait(false);

            var payload = new
            {
                asOfPeriod = result.WipUnbilledPeriod,
                revenueGenerationDetected = result.RevenueGenerationDetected,
                dataLoaded = result.DataLoaded,
                firmwide = new
                {
                    earned = result.FirmWipUnbilled,
                    overbilled = result.FirmWipOverbilled,
                    net = result.FirmWipNet,
                },
                drilldownTotals = new
                {
                    earned = result.WipUnbilled,
                    overbilled = result.WipOverbilled,
                    net = result.WipUnbilledNet,
                },
                topProjects = result.WipProjectRows.Take(50).Select(r => new
                {
                    wbs1 = r.Wbs1,
                    projectName = r.ProjectName,
                    clientId = r.ClientId,
                    clientName = r.ClientName,
                    pm = r.Pm,
                    earned = r.Earned,
                    overbilled = r.Overbilled,
                    net = r.Net,
                    period = r.Period,
                }),
                methodology =
                    "Canonical KOR WIP per WipFinancialsService. " +
                    "Two paths auto-selected: (a) if PRSummaryMain.Unbilled column is populated " +
                    "(Deltek Revenue Generation = ON, KOR's current config), use SUM(COALESCE(Unbilled,0)) " +
                    "cumulative <= asOfPeriod. (b) if not populated (Revenue Generation = OFF), " +
                    "proxy via SUM(Revenue - Billed) cumulative <= asOfPeriod. Both bucket " +
                    "by joined pr.Org (USA -> CAD at Financials.Billed.UsdToCadRate). Sign convention: " +
                    "positive Net = earned-not-invoiced; negative Net = overbilled. Firmwide totals and " +
                    "the per-project drilldown use the same branch and sums tie out. " +
                    "Per-project drilldown JOINs PR for project name, EMMain for PM, Clendor for client name - " +
                    "surface clientName not the raw clientId.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("get_wip", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_wip failed.");
            return ToolErrorEnvelope.FromException("get_wip", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_wip",
                InputJson: "{}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

}
