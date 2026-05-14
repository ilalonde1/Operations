#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.PMTools;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Single-project deep-dive by WBS1. Wraps ProjectAnalyticsService rows +
/// revenue timeline so the answer matches the WPF Historical Analytics
/// row detail by construction.
/// </summary>
[McpServerToolType]
public sealed class ProjectDetailTool
{
    private readonly ProjectAnalyticsService _projects;
    private readonly AuditLogger _audit;
    private readonly ILogger<ProjectDetailTool> _logger;

    public ProjectDetailTool(ProjectAnalyticsService projects, AuditLogger audit, ILogger<ProjectDetailTool> logger)
    {
        _projects = projects;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_project_detail")]
    [Description(
        "Get KOR-canonical full detail for one project by WBS1. Wraps ProjectAnalyticsService rows + revenue " +
        "timeline (same source as the WPF Historical Analytics row detail). Returns Pm/DraftingManager, " +
        "Phase/Status/Org, Fee/FeeBilled/PercentBilled, hours by type, AR aging, EstEngBudget vs actual + delta, " +
        "Margin/MarginPct, and per-period revenue timeline. Use this for any single-project question once a " +
        "WBS1 is known.")]
    public async Task<string> GetProjectDetailAsync(
        [Description("Required. Deltek WBS1 identifier (case-insensitive).")] string wbs1,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            if (string.IsNullOrWhiteSpace(wbs1))
            {
                errorMessage = "wbs1 is required.";
                return JsonError(errorMessage);
            }

            var rows = await Task.Run(() => _projects.LoadProjectRowsSync(cancellationToken), cancellationToken).ConfigureAwait(false);
            var timeline = await Task.Run(() => _projects.LoadRevenueTimelineSync(cancellationToken), cancellationToken).ConfigureAwait(false);
            _projects.AttachRevenueTimelines(rows, timeline);

            var target = wbs1.Trim();
            var row = rows.FirstOrDefault(r => string.Equals(r.Wbs1, target, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                errorMessage = $"No project found with WBS1 '{target}'.";
                return JsonError(errorMessage);
            }

            var payload = new
            {
                project = new
                {
                    wbs1 = row.Wbs1,
                    name = row.Name,
                    pm = row.Pm,
                    draftingManager = row.DraftingManager,
                    clientId = row.ClientId,
                    phase = row.Phase,
                    status = row.Status,
                    org = row.Org,
                    openDate = row.OpenDate?.ToString("yyyy-MM-dd"),
                    closeDate = row.CloseDate?.ToString("yyyy-MM-dd"),
                    durationMonths = row.DurationMonths,
                    fee = row.Fee,
                    hourlyRevenue = row.HourlyRevenue,
                    totalFee = row.TotalFee,
                    feeBilled = row.FeeBilled,
                    unpostedFeeBilled = row.UnpostedFeeBilled,
                    percentBilled = row.PercentBilled,
                    percentBilledWithUnposted = row.PercentBilledWithUnposted,
                    engHrs = row.EngHrs,
                    draftHrs = row.DraftHrs,
                    totalEngDraft = row.TotalEngDraft,
                    inspHrs = row.InspHrs,
                    docPrepHrs = row.DocPrepHrs,
                    genHrs = row.GenHrs,
                    adminHrs = row.AdminHrs,
                    nonBillHrs = row.NonBillHrs,
                    billableHrs = row.BillableHrs,
                    totalAllHrs = row.TotalAllHrs,
                    billablePct = row.BillablePct,
                    overheadRatio = row.OverheadRatio,
                    subCost = row.SubCost,
                    subPctOfFee = row.SubPctOfFee,
                    totalCost = row.TotalCost,
                    margin = row.Margin,
                    marginPct = row.MarginPct,
                    arTotal = row.ArTotal,
                    arCurrent = row.ArCurrent,
                    ar31To60 = row.Ar31To60,
                    ar61To90 = row.Ar61To90,
                    ar90Plus = row.Ar90Plus,
                    feePerHr = row.FeePerHr,
                    netFee = row.NetFee,
                    netFeePerHr = row.NetFeePerHr,
                    estEngBudget = row.EstEngBudget,
                    estDraftBudget = row.EstDraftBudget,
                    engBudgetDelta = row.EngBudgetDelta,
                    draftBudgetDelta = row.DraftBudgetDelta,
                    budgetPeerCount = row.BudgetPeerCount,
                    totalInspections = row.TotalInspections,
                    lastMonthInspections = row.LastMonthInspections,
                    constructionType = row.ConstructionType,
                    projectCategory = row.ProjectCategory,
                    draftingType = row.DraftingType,
                    revenueTimeline = row.RevenueTimeline?.Select(p => new
                    {
                        period = p.Period,
                        revenue = p.Revenue,
                        billed = p.Billed,
                    }),
                },
                methodology =
                    "Canonical KOR Historical Analytics row for one WBS1 from ProjectAnalyticsService. Includes " +
                    "Pm/DraftingManager, Fee/FeeBilled (currency-normalized via OrgFx, USA->CAD applied), " +
                    "hours by LaborCode bucket, AR aging from the AR table, EstEngBudget from target-rate or " +
                    "peer-budget estimator, and per-period revenue timeline from PRSummaryMain (BilledFee falls " +
                    "back to Revenue when zero, matching the canonical Historical Analytics revenue accounting).",
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
            _logger.LogWarning(ex, "get_project_detail failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_project_detail",
                InputJson: JsonSerializer.Serialize(new { wbs1 }),
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        ToolErrorEnvelope.Build("get_project_detail", message, errorClass: "Unknown", recoverable: true, durationMs: 0);
}
