#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Opportunities.Data.BdReports;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// BD pursuit list per sector — the SAME IBdReportService queries the WPF BD
/// Reports dashboard renders (BD-UI-Plan decision 4: no drift between what
/// the dashboard shows and what the AI answers).
/// </summary>
[McpServerToolType]
public sealed class BdPursuitListTool
{
    private readonly IBdReportService _reports;
    private readonly AuditLogger _audit;
    private readonly ILogger<BdPursuitListTool> _logger;

    public BdPursuitListTool(IBdReportService reports, AuditLogger audit, ILogger<BdPursuitListTool> logger)
    {
        _reports = reports;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_bd_pursuit_list")]
    [Description(
        "Get KOR's BD pursuit list for one sector or region from live honing verdicts (KorOpportunitiesDb). " +
        "Keys: hospitals, schools, indigenous, bc-housing, defense, recreational, residential, " +
        "commercial, post-secondary, okanagan, us-west. Returns the sector's verdict counts plus the top pursuits " +
        "(PURSUE_URGENT first, then PURSUE, then MONITOR/DISCOVER), each with proponent, cost, " +
        "honing status and the KOR angle. Verdicts come from " +
        "COALESCE(JSON_VALUE($.honingPass.verdict), JSON_VALUE($.verdict)) on ProjectBriefHoning " +
        "enrichments — identical to the BD Reports dashboard. ALWAYS use this for 'top N <sector> " +
        "pursuits', 'what should we chase in <sector>', 'hospital/school/residential pipeline' questions.")]
    public async Task<string> GetBdPursuitListAsync(
        [Description("Required. Sector/region key: hospitals | schools | indigenous | bc-housing | defense | recreational | residential | commercial | post-secondary | okanagan | us-west.")] string sectorKey,
        [Description("Optional. Max pursuit rows returned (default 15, cap 50).")] int? maxRows,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var inputJson = JsonSerializer.Serialize(new { sectorKey, maxRows });
        try
        {
            var take = Math.Clamp(maxRows ?? 15, 1, 50);
            var rows = await _reports.GetSectorPursuitsAsync(sectorKey, cancellationToken).ConfigureAwait(false);
            var honed = rows.Where(x => x.Verdict is not null).ToList();

            var payload = new
            {
                sectorKey,
                totalProjects = rows.Count,
                honedProjects = honed.Count,
                notYetHoned = rows.Count - honed.Count,
                verdictCounts = honed
                    .GroupBy(x => x.Verdict!)
                    .ToDictionary(g => g.Key, g => g.Count()),
                urgentCount = honed.Count(x => x.IsUrgent),
                pursuits = honed
                    .Where(x => x.Verdict is BdVerdicts.PursueUrgent or BdVerdicts.Pursue or BdVerdicts.Monitor or BdVerdicts.Discover)
                    .Take(take)
                    .Select(x => new
                    {
                        mpiId = x.MpiId,
                        project = x.ProjectName,
                        province = x.Province,
                        proponent = x.ProponentName,
                        cost = x.EstimatedCostText ?? x.EstimatedCostCad?.ToString("N0"),
                        verdict = x.Verdict,
                        isUrgent = x.IsUrgent,
                        status = Cap(x.HoningStatus, 200),
                        korAngle = Cap(x.KorAngle, 400),
                        lastRefreshUtc = x.LastRefreshAtUtc?.ToString("yyyy-MM-dd"),
                    }),
                methodology =
                    "Live from opportunities.MajorProjectsInventory (active rows) joined to the " +
                    "ProjectBriefHoning enrichment. Verdict = COALESCE(JSON_VALUE($.honingPass.verdict), " +
                    "JSON_VALUE($.verdict)); urgency = PURSUE_URGENT verdict OR PURSUE with 'URGENT' in the " +
                    "KOR angle (the shipped report builders' rule). Rows ordered verdict rank then cost desc. " +
                    "Identical query path to the WPF BD Reports dashboard — numbers always match it.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (ArgumentException ex)
        {
            sw.Stop();
            errorMessage = ex.Message;
            return JsonSerializer.Serialize(new { error = ex.Message, durationMs = (int)sw.ElapsedMilliseconds });
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("get_bd_pursuit_list", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_bd_pursuit_list failed.");
            return ToolErrorEnvelope.FromException("get_bd_pursuit_list", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_bd_pursuit_list",
                InputJson: inputJson,
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string? Cap(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
