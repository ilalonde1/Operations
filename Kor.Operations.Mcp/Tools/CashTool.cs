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
        "Get KOR-canonical cash position: latest CAD/USA/BCC bucket balances, combined CAD-equivalent, " +
        "12-month history, and per-account breakdown. Wraps CashFinancialsService (same code path as the " +
        "WPF Cash tile), so numbers match by construction. Use this instead of querying GLSummary+CFGBanks " +
        "directly - the canonical version applies the Financials.Cash.UsdAccounts override that reclassifies " +
        "individual accounts (e.g., a USD CHQ account inside a CAD entity counts as USA cash).")]
    public async Task<string> GetCashPositionAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var result = await _svc.LoadAsync(cancellationToken).ConfigureAwait(false);

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
                    "Canonical KOR cash position per CashFinancialsService. " +
                    "Cumulative GLSummary balances for CFGBanks-registered accounts, classified by " +
                    "Financials.Cash.UsdAccounts override first (per-account currency rule wins) then " +
                    "by Org bucket (CAD / USA / BCC). USA bucket FX-converted to CAD at " +
                    "Financials.Cash.UsdToCadRate. History is the last 12 cumulative monthly periods.",
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
            _logger.LogWarning(ex, "get_cash_position failed.");
            return JsonError(errorMessage);
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

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
