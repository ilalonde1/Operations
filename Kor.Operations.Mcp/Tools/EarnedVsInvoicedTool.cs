#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical Earned vs Invoiced comparison across the latest
/// 1 and last 3 closed PRSummaryMain periods. Earned = SUM(BilledFee else
/// Revenue) per period; Invoiced = SUM(PRSummaryMain.Billed) per period.
/// UnbilledGap = Earned - Invoiced. Wraps RecentBilledService so numbers
/// match the WPF Executive Summary tile by construction. Period-anchored
/// (NOT a literal 30/90-day calendar window - Deltek's ~3-month posting
/// lag would otherwise collapse a strict calendar window to ~$0).
/// </summary>
[McpServerToolType]
public sealed class EarnedVsInvoicedTool
{
    private readonly RecentBilledService _svc;
    private readonly AuditLogger _audit;
    private readonly ILogger<EarnedVsInvoicedTool> _logger;

    public EarnedVsInvoicedTool(RecentBilledService svc, AuditLogger audit, ILogger<EarnedVsInvoicedTool> logger)
    {
        _svc = svc;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_earned_vs_invoiced")]
    [Description(
        "Get KOR-canonical Earned vs Invoiced comparison firmwide across the latest 1 and last 3 " +
        "closed PRSummaryMain periods. Earned = SUM(BilledFee else Revenue); Invoiced = " +
        "SUM(PRSummaryMain.Billed); UnbilledGap = Earned - Invoiced. Wraps RecentBilledService so " +
        "numbers match the WPF Executive Summary tile by construction. NOTE: '30' / '90' suffixes " +
        "mean 'latest 1 period' / 'latest 3 periods' (period-anchored), NOT literal calendar " +
        "windows - Deltek's ~3-month posting lag would otherwise collapse a strict calendar " +
        "window to ~$0. Positive UnbilledGap = earned-but-not-yet-invoiced (billing runway). " +
        "Negative UnbilledGap = invoiced ahead of recognition (could be retainer/milestone billing).")]
    public async Task<string> GetEarnedVsInvoicedAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var result = await _svc.LoadAsync(cancellationToken).ConfigureAwait(false);

            var payload = new
            {
                latestPeriod = result.LatestPeriod,
                periodsUsed = result.Periods,
                dataLoaded = result.DataLoaded,
                usdToCadRate = result.UsdToCadRate,
                latest1Period = new
                {
                    earned = result.Earned30,
                    invoiced = result.Billed30,
                    unbilledGap = result.Earned30 - result.Billed30,
                },
                last3Periods = new
                {
                    earned = result.Earned90,
                    invoiced = result.Billed90,
                    unbilledGap = result.Earned90 - result.Billed90,
                },
                methodology =
                    "Canonical KOR Earned vs Invoiced per RecentBilledService. Earned = SUM(CASE " +
                    "WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) " +
                    "per period (legacy Revenue fallback for pre-2024 projects). Invoiced = " +
                    "SUM(COALESCE(sm.Billed, 0)) per period. Both Org-bucketed via joined PR " +
                    "(USA -> CAD at Financials.Billed.UsdToCadRate). 'latest1Period' = the latest " +
                    "closed PRSummaryMain period; 'last3Periods' = sum across the latest 3 closed " +
                    "periods. UnbilledGap = Earned - Invoiced. Period-anchored framing absorbs " +
                    "Deltek's ~3-month posting lag; a literal calendar window would collapse to ~$0.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("get_earned_vs_invoiced", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_earned_vs_invoiced failed.");
            return ToolErrorEnvelope.FromException("get_earned_vs_invoiced", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_earned_vs_invoiced",
                InputJson: "{}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

}
