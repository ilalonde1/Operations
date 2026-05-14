#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical firm-health snapshot (trailing 12 months).
/// Wraps FirmHealthService (same code path as the WPF Net Multiplier /
/// Labor Margin tiles). Returns NSR, DLC, Net Multiplier, Labor Margin
/// (a.k.a. NetProfit12Mo). DSO is left to the caller to compute via
/// get_ar + this tool's netServiceRevenue12Mo - the async path does not
/// re-fetch AR.
/// </summary>
[McpServerToolType]
public sealed class FirmHealthTool
{
    private readonly FirmHealthService _svc;
    private readonly AuditLogger _audit;
    private readonly ILogger<FirmHealthTool> _logger;

    public FirmHealthTool(FirmHealthService svc, AuditLogger audit, ILogger<FirmHealthTool> logger)
    {
        _svc = svc;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_firm_health")]
    [Description(
        "Get KOR-canonical firm-health KPIs (trailing 12 months): Net Service Revenue, " +
        "Direct Labor Cost, Net Multiplier (NSR/DLC), and Labor Margin (NSR-DLC, a.k.a. NetProfit12Mo). " +
        "Wraps FirmHealthService (same code path as the WPF Net Multiplier / Labor Margin tiles), " +
        "so numbers match by construction. Use this for 'how healthy is the firm', 'what is our net " +
        "multiplier', 'labor margin', 'are we beating ZweigGroup benchmarks', or anything else where " +
        "the canonical NSR/DLC pair is the right answer. For DSO, also call get_ar and combine the " +
        "two: DSO = (get_ar firmwideOutstandingCadEquiv / get_firm_health netServiceRevenue12Mo) * 365.")]
    public async Task<string> GetFirmHealthAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var result = await _svc.LoadAsync(cancellationToken).ConfigureAwait(false);

            var payload = new
            {
                netServiceRevenue12Mo = result.NetServiceRevenue12Mo,
                directLaborCost12Mo = result.DirectLaborCost12Mo,
                netMultiplier = result.NetMultiplier,
                laborMargin12Mo = result.NetProfit12Mo,
                dataLoaded = result.DataLoaded,
                benchmarks = new
                {
                    netMultiplierHealthy = 3.0,
                    netMultiplierStrong = 3.5,
                    netMultiplierMarginCompressed = 2.5,
                    source = "ZweigGroup AEC industry benchmarks",
                },
                methodology =
                    "Canonical KOR firm-health per FirmHealthService. " +
                    "NSR = SUM(-LedgerAR.Amount) trailing 12mo where TransType='IN' and Account prefix in " +
                    "{4001, 4003, 4210, 4220, 4240} (Daler's canonical revenue accounts), bucketed by Org " +
                    "(USA -> CAD at Financials.Billed.UsdToCadRate). DLC = SUM(tkDetail.RegAmt + OvtAmt + " +
                    "SpecialOvtAmt) trailing 12mo on direct projects (LaborCode NOT IN (70 Admin, 80 NonBillable); " +
                    "WBS1 NOT LIKE '[A-Z]%' or '9[A-Z]%' or '99%'; employee active; LineItemApprovalStatus <> 'R'), " +
                    "bucketed by pr.Org. NetMultiplier = NSR / DLC. LaborMargin = NSR - DLC (pre-overhead, NOT " +
                    "bottom-line profit). DSO is NOT returned here - compose with get_ar.",
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
            _logger.LogWarning(ex, "get_firm_health failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_firm_health",
                InputJson: "{}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        ToolErrorEnvelope.Build("get_firm_health", message, errorClass: "Unknown", recoverable: true, durationMs: 0);
}
