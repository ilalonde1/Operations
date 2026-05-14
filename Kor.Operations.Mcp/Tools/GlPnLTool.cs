#nullable enable
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical GL P&amp;L (posted). Wraps GlProfitLossService
/// (the same service the WPF GL P&amp;L screen uses), so MCP answers match
/// the screen exactly. Use this instead of having the LLM construct
/// ad-hoc GLSummary SUMs - those drift on KOR's GLTable groupings,
/// FX bucketing, and Income/Expense group-type filters.
/// </summary>
[McpServerToolType]
public sealed class GlPnLTool
{
    private readonly GlProfitLossService _svc;
    private readonly AuditLogger _audit;
    private readonly ILogger<GlPnLTool> _logger;

    public GlPnLTool(GlProfitLossService svc, AuditLogger audit, ILogger<GlPnLTool> logger)
    {
        _svc = svc;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_gl_pnl")]
    [Description(
        "Get KOR-canonical GL (posted) P&L totals + top expense/revenue account drivers for a period range. " +
        "Wraps GlProfitLossService (the same code path the WPF GL P&L screen uses). GL is posted with ~3-month " +
        "lag - tool surfaces the latest posted period in `maxPostedPeriod`. Amounts returned with sign flipped " +
        "to user-friendly convention (revenue positive, expenses positive); the methodology field documents this. " +
        "ALWAYS use this tool for GL P&L period totals, breakdowns, comparisons, and 'why is X high/low' " +
        "questions instead of querying GLSummary directly.")]
    public async Task<string> GetGlPnLAsync(
        [Description("Period start, inclusive. ISO 8601 (e.g. '2024-04-01').")] string periodStart,
        [Description("Period end, inclusive. ISO 8601 (e.g. '2024-04-30').")] string periodEnd,
        [Description("Org filter. MUST mirror the on-screen org filter when the user is asking from a Financials/Executive Summary window context (the BuildContext block will say 'Org filter: CAD' or similar - pass that EXACT value). Valid values: 'CAD' (Canadian entity, Vancouver), 'USA' (US entity, LA/San Diego), 'BCC' (third entity), or explicit null for combined CAD-equivalent firm-wide rollup. Empty string is REJECTED (returns an error) - do not pass empty as a shortcut for null. 'KOR' / 'KORUSA' are informal labels and will be rejected.")] string? org,
        [Description("How many top accounts to return in each section. Default 10, max 25.")] int? topN,
        [Description("Optional: specific GLTable TableNo to query. If null, the first Income-Statement table KOR has configured is used.")] short? tableNo,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            if (!DateTime.TryParse(periodStart, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var fromDate))
            {
                errorMessage = $"periodStart not a valid date: '{periodStart}'.";
                return ToolErrorEnvelope.Validation("get_gl_pnl", errorMessage, (int)sw.ElapsedMilliseconds);
            }

            if (!DateTime.TryParse(periodEnd, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var toDate))
            {
                errorMessage = $"periodEnd not a valid date: '{periodEnd}'.";
                return ToolErrorEnvelope.Validation("get_gl_pnl", errorMessage, (int)sw.ElapsedMilliseconds);
            }

            var n = Math.Clamp(topN ?? 10, 1, 25);
            // null = explicit firm-wide rollup; empty/whitespace = AI forgot to pass
            // the on-screen org filter (Apr-2024 incident, 2026-05-13). Reject empty
            // string so the model gets a tight loop-back error instead of a 2x
            // firm-wide answer it then narrates as CAD-only.
            string? orgFilter;
            if (org is null)
            {
                orgFilter = null;
            }
            else if (string.IsNullOrWhiteSpace(org))
            {
                errorMessage = "org parameter is empty string. Pass the EXACT on-screen org filter ('CAD' / 'USA' / 'BCC') or explicit null for combined firm-wide rollup. Empty string is invalid.";
                return ToolErrorEnvelope.Validation("get_gl_pnl", errorMessage, (int)sw.ElapsedMilliseconds);
            }
            else
            {
                orgFilter = org.Trim();
                if (!string.Equals(orgFilter, "CAD", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(orgFilter, "USA", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(orgFilter, "BCC", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = $"org value '{orgFilter}' is not a valid Deltek Org code. Use 'CAD', 'USA', 'BCC', or null. Values 'KOR'/'KORUSA' are informal labels and return zero rows.";
                    return ToolErrorEnvelope.Validation("get_gl_pnl", errorMessage, (int)sw.ElapsedMilliseconds);
                }
            }

            // Resolve tableNo if not provided.
            short resolvedTable = tableNo ?? 0;
            string? tableName = null;
            if (resolvedTable == 0)
            {
                var tables = await _svc.GetTablesAsync(cancellationToken).ConfigureAwait(false);
                var first = tables.FirstOrDefault();
                if (first == null)
                {
                    errorMessage = "No GLTable entries matched the configured Income Statement filter; cannot run GL P&L.";
                    return ToolErrorEnvelope.Validation("get_gl_pnl", errorMessage, (int)sw.ElapsedMilliseconds);
                }
                resolvedTable = first.TableNo;
                tableName = first.TableName;
            }

            var result = await _svc.BuildProfitLossAsync(
                resolvedTable,
                fromDate,
                toDate,
                orgFilter,
                flipSign: true,
                forceRefresh: false,
                cancellationToken).ConfigureAwait(false);

            // Sum a Summary/GrandTotal row across all period columns.
            decimal SumGrand(string lineItem)
            {
                decimal sum = 0m;
                foreach (DataRow row in result.Table.Rows)
                {
                    if (!string.Equals(Convert.ToString(row["RowKind"]), "GrandTotal", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.Equals(Convert.ToString(row["LineItem"]), lineItem, StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (var col in result.PeriodColumnNames)
                    {
                        if (row[col] is decimal d) sum += d;
                    }
                }
                return sum;
            }

            var revenue = SumGrand("Total Revenue");
            var expenses = SumGrand("Total Expenses");
            var net = SumGrand("Net Income");
            var margin = revenue == 0m ? 0m : net / revenue;

            // Top N detail rows by absolute amount across the period range.
            IEnumerable<object> TopDetails(Func<string, bool> sectionFilter, int take)
            {
                var rows = new List<(string section, string lineItem, short glGroup, decimal amount)>();
                foreach (DataRow row in result.Table.Rows)
                {
                    if (!string.Equals(Convert.ToString(row["RowKind"]), "Detail", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var section = Convert.ToString(row["Section"]) ?? "";
                    if (!sectionFilter(section)) continue;
                    var lineItem = Convert.ToString(row["LineItem"]) ?? "";
                    short glGroup = row["LineGroupCode"] is short s ? s : (short)0;
                    decimal total = 0m;
                    foreach (var col in result.PeriodColumnNames)
                    {
                        if (row[col] is decimal d) total += d;
                    }
                    rows.Add((section, lineItem, glGroup, total));
                }
                return rows
                    .OrderByDescending(r => Math.Abs(r.amount))
                    .Take(take)
                    .Select(r => (object)new { section = r.section, glGroup = r.glGroup, label = r.lineItem, amount = r.amount });
            }

            // Section name heuristic: revenue sections typically contain "Income" or "Revenue";
            // expense sections contain "Expense" or "Cost" or similar. We surface both buckets,
            // letting the LLM cite the section name verbatim.
            bool IsRevenueSection(string s) =>
                s.IndexOf("Revenue", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Income", StringComparison.OrdinalIgnoreCase) >= 0;
            bool IsExpenseSection(string s) =>
                s.IndexOf("Expense", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Cost", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Overhead", StringComparison.OrdinalIgnoreCase) >= 0;

            var payload = new
            {
                period = new { start = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                org = orgFilter,
                currency = orgFilter == "USA" ? "USD" : "CAD",
                glTable = new { tableNo = resolvedTable, tableName },
                totals = new { revenue, expenses, net, margin },
                topExpenseAccounts = TopDetails(IsExpenseSection, n),
                topRevenueAccounts = TopDetails(IsRevenueSection, n),
                maxPostedPeriod = result.MaxPostedPeriod,
                methodology =
                    "KOR canonical GL (posted) P&L per GlProfitLossService. " +
                    "Aggregates GLSummary by GLTable section/group for the configured Income Statement table. " +
                    "Revenue groups (default group-type 4/8) + Expense groups (default group-type 5/6/7). " +
                    "Amounts returned with sign flipped to user convention (revenue positive, expenses positive). " +
                    "GL is posted with ~3 month lag; `maxPostedPeriod` shows the latest period with posted data. " +
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
            return ToolErrorEnvelope.Cancelled("get_gl_pnl", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_gl_pnl failed.");
            return ToolErrorEnvelope.FromException("get_gl_pnl", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_gl_pnl",
                InputJson: $"{{\"periodStart\":\"{periodStart}\",\"periodEnd\":\"{periodEnd}\",\"org\":\"{org}\",\"topN\":{topN ?? 10},\"tableNo\":{tableNo?.ToString() ?? "null"}}}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

}
