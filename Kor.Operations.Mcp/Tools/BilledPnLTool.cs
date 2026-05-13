#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical Billed P&L. Wraps BilledFinancialsService
/// (the same service the WPF Billed P&L screen uses), so MCP-side
/// answers match the screen exactly. Use this instead of having the
/// LLM construct ad-hoc sub-ledger SUMs: those drift on KOR's
/// canonical exclusions/inclusions (7290, 7970, 8200, 8300).
/// </summary>
[McpServerToolType]
public sealed class BilledPnLTool
{
    private readonly BilledFinancialsService _svc;
    private readonly AuditLogger _audit;
    private readonly ILogger<BilledPnLTool> _logger;

    public BilledPnLTool(BilledFinancialsService svc, AuditLogger audit, ILogger<BilledPnLTool> logger)
    {
        _svc = svc;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_billed_pnl")]
    [Description(
        "Get KOR-canonical Billed P&L totals + top expense/revenue account drivers for a period range. " +
        "Uses BilledFinancialsService directly (same code path as the WPF Billed P&L screen), so the " +
        "numbers match the on-screen totals by construction. ALWAYS use this for Billed P&L period " +
        "totals, breakdowns, comparisons, and 'why is X high/low' questions instead of querying " +
        "LedgerAR/LedgerAP/LedgerEX/LedgerMisc directly - raw SQL will not reproduce KOR's canonical " +
        "predicates.")]
    public async Task<string> GetBilledPnLAsync(
        [Description("Period start, inclusive. ISO 8601 (e.g. '2024-04-01').")] string periodStart,
        [Description("Period end, inclusive. ISO 8601 (e.g. '2024-04-30').")] string periodEnd,
        [Description("Org filter: 'KOR' (CAD entity), 'KORUSA' (USA entity), or null for combined CAD-equivalent rollup.")] string? org,
        [Description("How many top accounts to return in each section. Default 10, max 25.")] int? topN,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            if (!DateTime.TryParse(periodStart, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var fromDate))
            {
                errorMessage = $"periodStart not a valid date: '{periodStart}'.";
                return JsonError(errorMessage);
            }

            if (!DateTime.TryParse(periodEnd, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var toDate))
            {
                errorMessage = $"periodEnd not a valid date: '{periodEnd}'.";
                return JsonError(errorMessage);
            }

            var n = Math.Clamp(topN ?? 10, 1, 25);
            var orgFilter = string.IsNullOrWhiteSpace(org) ? null : org.Trim();

            var result = await _svc.BuildAsync(
                fromDate,
                toDate,
                orgFilter,
                forceRefresh: false,
                cancellationToken).ConfigureAwait(false);

            decimal SumLine(string lineItem) =>
                result.Lines
                    .Where(l => string.Equals(l.RowKind, "SectionTotal", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(l.LineItem, lineItem, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(l => l.AmountsByPeriod.Values)
                    .Sum();

            var revenue = SumLine("Total Revenue");
            var expenses = SumLine("Total Expenses");
            var otherIncome = SumLine("Total Other Income");
            var net = revenue + otherIncome - expenses;
            var margin = revenue == 0m ? 0m : net / revenue;

            IEnumerable<object> TopN(string section, int take) =>
                result.Lines
                    .Where(l => string.Equals(l.Section, section, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(l.RowKind, "Detail", StringComparison.OrdinalIgnoreCase))
                    .Select(l => new
                    {
                        account = l.Account ?? "",
                        label = l.LineItem,
                        amount = l.AmountsByPeriod.Values.Sum(),
                    })
                    .OrderByDescending(x => Math.Abs(x.amount))
                    .Take(take)
                    .Cast<object>();

            var payload = new
            {
                period = new { start = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                org = orgFilter,
                currency = orgFilter is null or "KOR" ? "CAD" : "USD",
                totals = new { revenue, expenses, otherIncome, net, margin },
                topExpenseAccounts = TopN("Expenses", n),
                topRevenueAccounts = TopN("Revenue", n),
                topOtherIncomeAccounts = TopN("Other Income", n),
                maxBilledPeriod = result.MaxBilledPeriod,
                methodology =
                    "KOR canonical Billed P&L per BilledFinancialsService (Batch 73). " +
                    "Revenue = LedgerAR signed-inverted on accounts {4001,4003,4210,4220,4240}, exclude 4260. " +
                    "Expenses = LedgerAP+LedgerEX+LedgerMisc over 5xxx+6xxx+7xxx+8200+8300, exclude 7290 (Deltek suspense) and 7970 (FX G&L). " +
                    "Other Income = 8xxx (excluding 8200/8300 reclassified to Expenses) plus 7970. " +
                    "USA-org rows FX-converted to CAD when org is null.",
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
            _logger.LogWarning(ex, "get_billed_pnl failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_billed_pnl",
                InputJson: $"{{\"periodStart\":\"{periodStart}\",\"periodEnd\":\"{periodEnd}\",\"org\":\"{org}\",\"topN\":{topN ?? 10}}}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
