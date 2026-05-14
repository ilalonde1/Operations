#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.PMTools;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Firmwide billable utilization broken out by calendar year. Wraps
/// FirmAnalyticsService (canonical Staff Utilization source), so per-year
/// percentages match the WPF YoY Trend tab by construction.
/// </summary>
[McpServerToolType]
public sealed class FirmUtilizationByYearTool
{
    private readonly FirmAnalyticsService _firm;
    private readonly AuditLogger _audit;
    private readonly ILogger<FirmUtilizationByYearTool> _logger;

    public FirmUtilizationByYearTool(FirmAnalyticsService firm, AuditLogger audit, ILogger<FirmUtilizationByYearTool> logger)
    {
        _firm = firm;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_firm_utilization_by_year")]
    [Description(
        "Get KOR-canonical firmwide billable utilization broken out by calendar year. Wraps " +
        "FirmAnalyticsService (same tkDetail aggregation that drives the WPF YoY Trend tab's " +
        "FirmBillablePct column). Each year row reports TotalHrs, BillableHrs, and BillablePct. " +
        "Billable excludes LaborCode 70 (Admin) and 80 (NonBillable) plus overhead WBS prefixes " +
        "([A-Z]%, 9[A-Z]%, 99%). Also returns the all-time totals. Use this for multi-year " +
        "utilization trend, year-over-year billable% comparison, or firm-direction questions.")]
    public async Task<string> GetFirmUtilizationByYearAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var stats = await Task.Run(() => _firm.LoadFirmUtilizationSync(cancellationToken), cancellationToken).ConfigureAwait(false);

            var years = stats.ByYear
                .OrderByDescending(kvp => kvp.Key)
                .Select(kvp => new
                {
                    year = kvp.Key,
                    totalHrs = kvp.Value.Total,
                    billableHrs = kvp.Value.Billable,
                    billablePct = kvp.Value.Total > 0 ? kvp.Value.Billable / kvp.Value.Total : 0,
                })
                .ToList();

            var payload = new
            {
                allTimeTotalHrs = stats.TotalHrs,
                allTimeBillableHrs = stats.BillableHrs,
                allTimeBillablePct = stats.BillablePct,
                yearCount = years.Count,
                rows = years,
                methodology =
                    "Canonical KOR firmwide utilization per FirmAnalyticsService. Aggregates tkDetail RegHrs + " +
                    "OvtHrs + SpecialOvtHrs by YEAR(TransDate). Billable excludes LaborCode 70 (Admin) and 80 " +
                    "(NonBillable) and overhead WBS prefixes ([A-Z]%, 9[A-Z]%, 99%). Approved time only " +
                    "(LineItemApprovalStatus != 'R'). Same calculation as the WPF YoY Trend tab's " +
                    "FirmBillablePct column.",
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
            _logger.LogWarning(ex, "get_firm_utilization_by_year failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_firm_utilization_by_year",
                InputJson: "{}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        JsonSerializer.Serialize(new { error = message });
}
