#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Mcp.Audit;
using Kor.Opportunities.Data.BdReports;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// READ-ONLY BD action tracking rollup. Status writes stay in the WPF UI
/// (IntelPersistenceService.SetActionStatusAsync) — AI write tools are
/// deferred per the Phase 4a read-only rule.
/// </summary>
[McpServerToolType]
public sealed class BdActionStatusTool
{
    private readonly IBdReportService _reports;
    private readonly AuditLogger _audit;
    private readonly ILogger<BdActionStatusTool> _logger;

    public BdActionStatusTool(IBdReportService reports, AuditLogger audit, ILogger<BdActionStatusTool> logger)
    {
        _reports = reports;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_bd_action_status")]
    [Description(
        "Get the READ-ONLY rollup of KOR's BD intel actions (opportunities.IntelAction): counts by " +
        "Status (Open/Done/Dismissed) × ActionType (PursuitAngle, ContactStrategy, TimingWindow, " +
        "HowToGetOnRoster, KorDisplacementRead, Other), plus the most recently updated open actions " +
        "with org, recommendation and target person. Use for 'how many BD actions are open', " +
        "'what actions are outstanding', 'BD follow-ups' questions. This tool CANNOT change action " +
        "status — marking Done/Dismissed happens in the app's BD Dashboard Priority Actions queue.")]
    public async Task<string> GetBdActionStatusAsync(
        [Description("Optional. Max open actions returned (default 15, cap 50).")] int? maxOpenActions,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        var inputJson = JsonSerializer.Serialize(new { maxOpenActions });
        try
        {
            var take = Math.Clamp(maxOpenActions ?? 15, 1, 50);
            var rollup = await _reports.GetActionRollupAsync(take, cancellationToken).ConfigureAwait(false);

            var payload = new
            {
                countsByStatus = rollup.Counts
                    .GroupBy(c => c.Status)
                    .ToDictionary(g => g.Key, g => g.Sum(c => c.Count)),
                countsByStatusAndType = rollup.Counts.Select(c => new { c.Status, c.ActionType, c.Count }),
                topOpenActions = rollup.TopOpenActions.Select(a => new
                {
                    actionId = a.ActionId,
                    org = a.OrgName,
                    actionType = a.ActionType,
                    recommendation = a.Recommendation,
                    targetPerson = a.TargetPersonName,
                    timingNotes = a.TimingNotes,
                }),
                methodology =
                    "Live from opportunities.IntelAction on active (non-retired) canonical orgs, " +
                    "non-retired actions only. Open actions ordered by UpdatedAtUtc desc, recommendations " +
                    "capped at 300 chars. Status writes go through the WPF BD Dashboard (Done/Dismissed " +
                    "buttons) — this tool is read-only by design.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return ToolErrorEnvelope.Cancelled("get_bd_action_status", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_bd_action_status failed.");
            return ToolErrorEnvelope.FromException("get_bd_action_status", ex, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_bd_action_status",
                InputJson: inputJson,
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }
}
