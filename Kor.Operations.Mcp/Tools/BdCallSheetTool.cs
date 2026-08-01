#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Opportunities.Data.BdReports;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Cross-sector URGENT call sheet — same pool the WPF Weekly Call Sheet
/// report renders (BD-UI-Plan decision 4: no drift).
/// </summary>
[McpServerToolType]
public sealed class BdCallSheetTool
{
    private readonly IBdReportService _reports;
    private readonly AuditLogger _audit;
    private readonly ILogger<BdCallSheetTool> _logger;

    public BdCallSheetTool(IBdReportService reports, AuditLogger audit, ILogger<BdCallSheetTool> logger)
    {
        _reports = reports;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_bd_call_sheet")]
    [Description(
        "Get KOR's cross-sector weekly BD call sheet: every active project honed PURSUE_URGENT or " +
        "PURSUE across ALL sectors (deduped), urgent items first then by project value, plus per-sector " +
        "verdict summaries. This is 'what should KOR act on THIS WEEK'. Same data as the WPF Weekly " +
        "Call Sheet report. ALWAYS use this for 'what do we act on this week', 'urgent BD items', " +
        "'call sheet', 'top pursuits across all sectors' questions.")]
    public async Task<string> GetBdCallSheetAsync(
        [Description("Optional. Max urgent + pursue rows returned (default 20, cap 60).")] int? maxRows,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var inputJson = JsonSerializer.Serialize(new { maxRows });
        try
        {
            var take = Math.Clamp(maxRows ?? 20, 1, 60);
            var pool = await _reports.GetCallSheetPoolAsync(cancellationToken).ConfigureAwait(false);
            var summaries = await _reports.GetSectorSummariesAsync(cancellationToken).ConfigureAwait(false);

            var payload = new
            {
                urgentCount = pool.Count(x => x.IsUrgent),
                pursueCount = pool.Count(x => !x.IsUrgent),
                items = pool.Take(take).Select(x => new
                {
                    mpiId = x.MpiId,
                    project = x.ProjectName,
                    province = x.Province,
                    municipality = x.MunicipalityName,
                    proponent = x.ProponentName,
                    cost = x.EstimatedCostText ?? x.EstimatedCostCad?.ToString("N0"),
                    isUrgent = x.IsUrgent,
                    status = Cap(x.HoningStatus, 200),
                    korAngle = Cap(x.KorAngle, 400),
                }),
                sectorStatus = summaries.Select(s => new
                {
                    sector = s.SectorKey,
                    honed = s.Total - s.NoVerdict,
                    total = s.Total,
                    pursueUrgent = s.PursueUrgent,
                    pursue = s.Pursue,
                    monitor = s.Monitor,
                    discover = s.Discover,
                }),
                methodology =
                    "Pool = active MPIs whose ProjectBriefHoning verdict is PURSUE_URGENT or PURSUE " +
                    "(COALESCE(JSON_VALUE($.honingPass.verdict), JSON_VALUE($.verdict))), no sector filter " +
                    "so cross-sector and deduped by construction; urgency additionally includes PURSUE rows " +
                    "whose KOR angle mentions URGENT. Ordered urgent-first then estimated cost desc. Sector " +
                    "summaries use the same overlapping sector filters as the dashboard cards — they do not " +
                    "sum to the distinct pool. Identical query path to the WPF Weekly Call Sheet.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("get_bd_call_sheet", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_bd_call_sheet failed.");
            return ToolErrorEnvelope.FromException("get_bd_call_sheet", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_bd_call_sheet",
                InputJson: inputJson,
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string? Cap(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
