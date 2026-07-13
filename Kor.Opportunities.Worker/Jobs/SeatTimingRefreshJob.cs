#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Kor.Opportunities.Worker.Services.Research;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Jobs;

/// <summary>
/// Keeps the attack sheet's SE-seat timing honest. Each run re-checks (via the
/// Anthropic web-search engine) the OLDEST sheet-relevant plays whose timing is
/// past the staleness threshold, and stamps SeatWindowCheckedAtUtc. The sheet
/// only badges/ranks 'now' when the timing is fresh, so an un-rechecked play
/// self-demotes — no stale confidence. Small daily budget; a no-op until timing
/// actually ages, so it costs nothing while the data is fresh.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SeatTimingRefreshJob : IJob
{
    private const string Provider = "SeatTimingRefresh";

    private const string SystemPrompt =
        "You research WHEN the structural-engineer (SE) seat opens on a construction project, " +
        "for a structural firm's business development. Use web search. Be current and precise; " +
        "prefer the most recent public evidence (procurement notices, board reports, news).";

    private const string Schema =
        "{\"type\":\"object\",\"properties\":{" +
        "\"window\":{\"type\":\"string\",\"enum\":[\"now\",\"2026\",\"2027+\",\"filled\",\"on-hold\"]}," +
        "\"incumbentSE\":{\"type\":\"string\"}," +
        "\"note\":{\"type\":\"string\"}}," +
        "\"required\":[\"window\"]}";

    private const string FormatInstruction =
        "Return the current SE-seat status. window: 'now' = seat being filled now (team forming / " +
        "active design / imminent or open procurement); '2026' = SE selection expected in 2026; " +
        "'2027+' = 2027 or later, or still concept/master-plan; 'filled' = an SE is already engaged " +
        "(put the firm in incumbentSE); 'on-hold' = paused, cancelled, or already built. Base it on " +
        "current web evidence, not the input.";

    private readonly IOptions<SeatTimingRefreshOptions> _options;
    private readonly IOptions<OpportunitiesWorkerOptions> _worker;
    private readonly IResearchExecutorService _executor;
    private readonly ILogger<SeatTimingRefreshJob> _logger;

    public SeatTimingRefreshJob(
        IOptions<SeatTimingRefreshOptions> options,
        IOptions<OpportunitiesWorkerOptions> worker,
        IResearchExecutorService executor,
        ILogger<SeatTimingRefreshJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private sealed record Candidate(long Id, string Name, string City, string Prov, string Sector, string Architect, string Owner, string CostText, string Stage);

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.Enabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration", nameof(SeatTimingRefreshJob));
            return;
        }

        var ct = context.CancellationToken;
        var db = _worker.Value.OpportunitiesDb!;
        var candidates = await LoadCandidatesAsync(db, Math.Clamp(opt.MaxPerRun, 1, 25), Math.Clamp(opt.StaleDays, 7, 365), ct).ConfigureAwait(false);

        int rechecked = 0, stillOpen = 0, closed = 0, failed = 0;
        long outputTok = 0;

        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (outputTok >= opt.DailyOutputTokenBudget)
            {
                _logger.LogInformation("{Job}: output-token budget reached ({Tok}); stopping.", nameof(SeatTimingRefreshJob), outputTok);
                break;
            }

            var user =
                $"Project: {c.Name}\nLocation: {c.City}, {c.Prov}\nSector: {c.Sector}\nArchitect: {c.Architect}\n" +
                $"Owner/Proponent: {c.Owner}\nEstimated cost: {c.CostText}\nStage (may be stale): {c.Stage}\n\n" +
                "Determine the CURRENT structural-engineer seat status for this project.";

            ExecutedResearch? res;
            try
            {
                res = await _executor.ExecuteAsync(
                    new ResearchTarget(c.Id, c.Name, c.Sector, Provider, SystemPrompt, user, Schema, FormatInstruction),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _logger.LogWarning(ex, "{Job}: research failed for MPI {Id}", nameof(SeatTimingRefreshJob), c.Id);
                continue;
            }

            if (res is null) { failed++; continue; }
            outputTok += res.OutputTokens;

            var (window, incumbent) = ParseResult(res.ResultJson);
            if (window is null) { failed++; continue; }

            try
            {
                var open = await ApplyAsync(db, c.Id, window, incumbent, ct).ConfigureAwait(false);
                rechecked++;
                if (open) stillOpen++; else closed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _logger.LogWarning(ex, "{Job}: persist failed for MPI {Id}", nameof(SeatTimingRefreshJob), c.Id);
            }
        }

        var summary = $"considered={candidates.Count}; rechecked={rechecked}; stillOpen={stillOpen}; closed={closed}; failed={failed}; outputTok={outputTok}";
        _logger.LogInformation("{Job}: {Summary}", nameof(SeatTimingRefreshJob), summary);
        context.Result = summary;
    }

    private static async Task<IReadOnlyList<Candidate>> LoadCandidatesAsync(string db, int max, int staleDays, CancellationToken ct)
    {
        // Sheet-relevant plays (channel known, architect known, seat open, source-fresh)
        // whose timing was never checked or is past the staleness window — oldest first.
        const string sql = @"
SELECT TOP (@max)
    m.Id, ISNULL(m.MunicipalityName,''), ISNULL(m.Province,''), ISNULL(m.Sector,''),
    ISNULL(m.ArchitectName,''), ISNULL(m.ProponentName,''), ISNULL(m.EstimatedCostText,''), ISNULL(m.Stage,''), ISNULL(m.ProjectName,'')
FROM opportunities.vw_ActionableProjects m
WHERE m.KorPipelineTag LIKE 'SE-channel:%' AND m.KorPipelineTag <> 'SE-channel:unknown'
  AND NULLIF(LTRIM(RTRIM(m.ArchitectName)),'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(m.StructuralEngineerName)),'') IS NULL
  AND COALESCE(m.LastVerifiedAtUtc, m.LastSeenAtUtc, m.UpdatedAtUtc) >= DATEADD(DAY, -45, SYSDATETIMEOFFSET())
  AND (m.SeatWindowCheckedAtUtc IS NULL OR m.SeatWindowCheckedAtUtc < DATEADD(DAY, -@stale, SYSDATETIMEOFFSET()))
ORDER BY CASE m.SeatWindow WHEN 'now' THEN 0 WHEN '2026' THEN 1 ELSE 2 END,
         ISNULL(m.SeatWindowCheckedAtUtc, '1900-01-01') ASC, m.Id;";

        var list = new List<Candidate>();
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = max;
        cmd.Parameters.Add("@stale", SqlDbType.Int).Value = staleDays;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new Candidate(
                Convert.ToInt64(r.GetValue(0)), r.GetString(8), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7)));
        }
        return list;
    }

    private static (string? Window, string? Incumbent) ParseResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var w = root.TryGetProperty("window", out var we) ? we.GetString()?.Trim().ToLowerInvariant() : null;
            if (w is not ("now" or "2026" or "2027+" or "filled" or "on-hold")) return (null, null);
            string? inc = root.TryGetProperty("incumbentSE", out var ie) ? ie.GetString() : null;
            return (w, string.IsNullOrWhiteSpace(inc) ? null : inc.Trim());
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>Writes the refreshed timing; returns true if the seat is still open
    /// (stays on the sheet), false if it closed off (filled / on-hold).</summary>
    private static async Task<bool> ApplyAsync(string db, long id, string window, string? incumbent, CancellationToken ct)
    {
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);

        if (window is "now" or "2026" or "2027+")
        {
            const string sql = @"
UPDATE opportunities.MajorProjectsInventory
   SET SeatWindow = @w, SeatWindowCheckedAtUtc = SYSDATETIMEOFFSET()
 WHERE Id = @id;";
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
            cmd.Parameters.Add("@w", SqlDbType.NVarChar, 20).Value = window;
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }

        // filled / on-hold: the seat is gone — take it off the sheet (SeatStatus),
        // record the incumbent if we learned one (fill-only), and stamp the check.
        const string closeSql = @"
UPDATE opportunities.MajorProjectsInventory
   SET SeatStatus = N'filled',
       SeatWindowCheckedAtUtc = SYSDATETIMEOFFSET(),
       StructuralEngineerName = COALESCE(NULLIF(LTRIM(RTRIM(StructuralEngineerName)), ''), @inc)
 WHERE Id = @id;";
        await using var c2 = new SqlCommand(closeSql, con) { CommandTimeout = 30 };
        c2.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        c2.Parameters.Add("@inc", SqlDbType.NVarChar, 200).Value = (object?)incumbent ?? DBNull.Value;
        await c2.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return false;
    }
}
