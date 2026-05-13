#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical AR position. Wraps ArFinancialsService (same
/// SQL the WPF AR tile uses), so MCP answers match the screen by
/// construction. Returns firmwide totals (CAD-equiv, USA bucket, CAD
/// bucket, Over60), top open projects with aging buckets, top open
/// invoices with daysPastDue + client name. NOTE: the MCP-side call does
/// NOT apply the Phase 12-5a in-collections split - that needs a
/// CollectionsRepository scan that's not wired in here yet. Consumers
/// who need Regular vs InCollections should call the WPF tile.
/// </summary>
[McpServerToolType]
public sealed class ArTool
{
    private readonly ArFinancialsService _svc;
    private readonly AuditLogger _audit;
    private readonly ILogger<ArTool> _logger;

    public ArTool(ArFinancialsService svc, AuditLogger audit, ILogger<ArTool> logger)
    {
        _svc = svc;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_ar")]
    [Description(
        "Get KOR-canonical AR (accounts-receivable) position firmwide: open balance in CAD-equiv, " +
        "over-60 AND over-90 aging (firmwideOver60CadEquiv, firmwideOver90CadEquiv), CAD vs USA org " +
        "split, top open projects with aging buckets (Current / 31-60 / 61-90 / 90+), and top open " +
        "invoices with daysPastDue + client name. " +
        "Wraps ArFinancialsService (same code path as the WPF AR tile), so numbers match by construction. " +
        "Use this instead of querying AR + PR + Clendor directly - the canonical version applies the " +
        "Org-bucketed FX conversion (USA -> CAD at Financials.Billed.UsdToCadRate) that ad-hoc SUMs miss.")]
    public async Task<string> GetArAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var result = await _svc.LoadAsync(cancellationToken).ConfigureAwait(false);

            // FirmwideOver90CadEquiv: sum Aged90Plus across all ProjectRows
            // (ArFinancialsResult exposes 60+ but not 90+ at the firmwide
            // level; AI can't reconstruct it from the top-25 ProjectRows
            // payload alone, which led to Batch 91 smoke test #6 failing on
            // an AR-90+ question. Surfacing it directly keeps narrative
            // answers from improvising around the gap.)
            var firmwideOver90 = result.ProjectRows.Sum(r => r.Aged90Plus);
            var payload = new
            {
                firmwideOutstandingCadEquiv = result.FirmwideOutstandingCadEquiv,
                firmwideOver60CadEquiv = result.FirmwideOver60CadEquiv,
                firmwideOver90CadEquiv = firmwideOver90,
                firmwideOutstandingCad = result.FirmwideOutstandingCad,
                firmwideOutstandingUsa = result.FirmwideOutstandingUsa,
                usdToCadRate = result.UsdToCadRate,
                topProjects = result.ProjectRows.Take(25).Select(r => new
                {
                    wbs1 = r.Wbs1,
                    projectName = r.ProjectName,
                    pm = r.Pm,
                    total = r.Total,
                    current = r.Current,
                    aged31To60 = r.Aged31To60,
                    aged61To90 = r.Aged61To90,
                    aged90Plus = r.Aged90Plus,
                    oldestInvoiceDate = r.OldestInvoiceDate,
                }),
                topInvoices = result.InvoiceRows.Take(50).Select(i => new
                {
                    wbs1 = i.Wbs1,
                    projectName = i.ProjectName,
                    pm = i.Pm,
                    invoice = i.Invoice,
                    clientId = i.ClientId,
                    clientName = i.ClientName,
                    invoiceDate = i.InvoiceDate,
                    dueDate = i.DueDate,
                    daysPastDue = i.DaysPastDue,
                    balance = i.Balance,
                }),
                methodology =
                    "Canonical KOR AR position per ArFinancialsService. " +
                    "Firmwide SUM(AR.InvBalanceSourceCurrency) bucketed by joined pr.Org (USA -> CAD at " +
                    "Financials.Billed.UsdToCadRate). Over60 = balances where DATEDIFF(day, " +
                    "COALESCE(DueDate, InvoiceDate), Today) > 60. Project rows aggregated by WBS1 + Org " +
                    "(FX applied per row). Invoice detail joins Clendor for client name. " +
                    "Aging anchor is COALESCE(DueDate, InvoiceDate) - same anchor used by the CRM and " +
                    "alert pipelines.",
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
            _logger.LogWarning(ex, "get_ar failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_ar",
                InputJson: "{}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
