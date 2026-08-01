#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical cash position. Wraps CashFinancialsService (same
/// SQL the WPF cash tile uses), so MCP-side answers match the screen by
/// construction. Use this instead of having the LLM construct ad-hoc
/// GLSummary+CFGBanks SUMs - those drift on KOR's per-account currency
/// overrides (e.g., 1120 Scotiabank USD CHQ lives under Org='CAD' but
/// counts as USA).
/// </summary>
[McpServerToolType]
public sealed class CashTool
{
    private readonly CashFinancialsService _svc;
    private readonly AuditLogger _audit;
    private readonly ILogger<CashTool> _logger;

    public CashTool(CashFinancialsService svc, AuditLogger audit, ILogger<CashTool> logger)
    {
        _svc = svc;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_cash_position")]
    [Description(
        "Get KOR-canonical real-time cash position: latest CAD/USA/BCC bucket balances (GLSummary cumulative " +
        "+ unposted sub-ledger overlay), combined CAD-equivalent, 12-month posted history, and per-account " +
        "breakdown. Wraps CashFinancialsService (same code path as the WPF Cash tile). Always use this " +
        "instead of querying GLSummary+CFGBanks directly - the canonical version layers a LedgerAR+AP+EX+Misc " +
        "overlay (TransDate > last closed GL period) onto cumulative GLSummary so the answer matches the " +
        "accountant's real-time balance sheet; Deltek's GL has ~3-month posting lag at KOR and a pure " +
        "GLSummary cumulative would silently understate cash by months of activity.")]
    public async Task<string> GetCashPositionAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var result = await _svc.LoadAsync(cancellationToken).ConfigureAwait(false);

            // Keep the LLM-facing JSON contract stable while the backing
            // Business records are CashAccountBalanceRow / CashHistoryPoint.
            var payload = new
            {
                period = result.Period,
                buckets = new
                {
                    cad = result.Cad,
                    usa = result.Usa,
                    bcc = result.Bcc,
                    combinedCadEquivalent = result.CombinedCadEquivalent,
                },
                unpostedOverlay = new
                {
                    cad = result.UnpostedOverlay.Cad,
                    usa = result.UnpostedOverlay.Usa,
                    bcc = result.UnpostedOverlay.Bcc,
                },
                usdToCadRate = result.UsdToCadRate,
                perAccount = result.PerAccount
                    .OrderByDescending(a => Math.Abs(a.Balance))
                    .Select(a => new
                    {
                        company = a.Company,
                        account = a.Account,
                        org = a.Org,
                        currency = a.Currency,
                        balance = a.Balance,
                    }),
                history = result.History.Select(h => new
                {
                    period = h.Period,
                    cad = h.Cad,
                    usa = h.Usa,
                    bcc = h.Bcc,
                    totalCadEquivalent = h.Cad + (h.Usa * result.UsdToCadRate) + h.Bcc,
                }),
                methodology =
                    "Canonical KOR cash position per CashFinancialsService (real-time, Batch 105). " +
                    "Posted layer = cumulative GLSummary.Amount for CFGBanks-registered accounts through the " +
                    "latest closed period, bucketed by Org (CAD / USA / BCC); USA bucket FX-converted to CAD at " +
                    "Financials.Cash.UsdToCadRate. Unposted overlay = SUM(LedgerAR + LedgerAP + LedgerEX + " +
                    "LedgerMisc).Amount with TransDate > end of the latest closed period, added to the headline " +
                    "buckets so the answer matches the accountant's real-time balance sheet (Deltek's GL ~3-month " +
                    "posting lag would otherwise miss months of activity). Deltek already records each bank " +
                    "account's GLSummary amount in its home-org functional currency, so no per-account FX " +
                    "reclassification is applied (Financials.Cash.UsdAccounts override is empty by default). " +
                    "History array is the last 12 GL-only cumulative monthly periods (overlay applies only to the " +
                    "headline). The unpostedOverlay field surfaces the overlay amounts so the LLM can explain " +
                    "the gap between posted-only and real-time numbers.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("get_cash_position", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_cash_position failed.");
            return ToolErrorEnvelope.FromException("get_cash_position", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_cash_position",
                InputJson: "{}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

}
