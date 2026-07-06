#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services.Reporting;

/// <summary>
/// Daily 6am "quick eye on the whole system" email (Ian, 2026-06-11):
/// new opportunities, verdict movement, retirements, per-pipeline ingest
/// activity, failures, coverage tickers, the latest nightly trigger report
/// (pending drain queues) via opportunities.QueueRefreshReport, and the
/// summary-flags harvest over new QueueDrain SUMMARY files.
/// Read-only against KorOpportunitiesDb; sends via the FileSync Graph app.
/// </summary>
[DisallowConcurrentExecution]
public sealed class BdMorningReportJob : IJob
{
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly GraphMailSender _mail;
    private readonly ILogger<BdMorningReportJob> _logger;

    public BdMorningReportJob(
        IOptions<OpportunitiesWorkerOptions> options,
        GraphMailSender mail,
        ILogger<BdMorningReportJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mail = mail ?? throw new ArgumentNullException(nameof(mail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.MorningReportEnabled)
        {
            _logger.LogDebug("{Job} skipped: disabled.", nameof(BdMorningReportJob));
            return;
        }

        if (string.IsNullOrWhiteSpace(opt.MorningReportTenantId)
            || string.IsNullOrWhiteSpace(opt.MorningReportClientId)
            || string.IsNullOrWhiteSpace(opt.MorningReportClientSecret))
        {
            _logger.LogWarning(
                "{Job} skipped: Graph credentials missing (KOR_OPPORTUNITIES_MORNINGREPORT* env vars).",
                nameof(BdMorningReportJob));
            return;
        }

        try
        {
            var html = await BuildHtmlAsync(opt.OpportunitiesDb, opt.BdResearchQueueDrainRoot, context.CancellationToken).ConfigureAwait(false);
            await _mail.SendHtmlAsync(
                opt.MorningReportTenantId,
                opt.MorningReportClientId,
                opt.MorningReportClientSecret,
                opt.MorningReportSenderUpn,
                opt.MorningReportRecipient,
                $"KOR BD Morning Report — {DateTime.Now:ddd MMM d}",
                html,
                context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Quartz must not retry-spin a broken mail config; log and wait
            // for tomorrow's fire.
            _logger.LogError(ex, "{Job} failed.", nameof(BdMorningReportJob));
        }
    }

    private static async Task<string> BuildHtmlAsync(string cs, string queueDrainRoot, System.Threading.CancellationToken ct)
    {
        await using var con = new SqlConnection(cs);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#1a1a1a;max-width:720px\">");
        sb.Append("<h2 style=\"border-bottom:3px solid #C8102E;padding-bottom:6px\">KOR BD Morning Report</h2>");
        sb.Append($"<p style=\"color:#666\">Generated {DateTime.Now:yyyy-MM-dd HH:mm} from live KorOpportunitiesDb. Window: last 24 hours.</p>");

        // --- New opportunities -------------------------------------------------
        var newMpis = new List<(string Name, string Province, string Cost)>();
        await using (var cmd = new SqlCommand(@"
SELECT TOP 15 m.ProjectName, COALESCE(m.Province, N'?'),
       COALESCE(m.EstimatedCostText, FORMAT(m.EstimatedCostCad, 'C0'), N'')
FROM opportunities.MajorProjectsInventory m
WHERE m.RetiredAtUtc IS NULL AND m.FirstSeenAtUtc >= DATEADD(hour, -24, sysdatetimeoffset())
ORDER BY m.EstimatedCostCad DESC;", con))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                newMpis.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
            }
        }

        int newMpiTotal;
        await using (var cmd = new SqlCommand(@"
SELECT COUNT(*) FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND FirstSeenAtUtc >= DATEADD(hour, -24, sysdatetimeoffset());", con))
        {
            newMpiTotal = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        sb.Append($"<h3>New opportunities <span style=\"color:#666;font-weight:normal\">({newMpiTotal})</span></h3>");
        if (newMpiTotal == 0) { sb.Append("<p style=\"color:#666\">None in the last 24h.</p>"); }
        else
        {
            sb.Append("<table style=\"border-collapse:collapse;width:100%\">");
            foreach (var (name, prov, cost) in newMpis)
            {
                sb.Append($"<tr><td style=\"padding:3px 8px;border-bottom:1px solid #eee\">{WebUtility.HtmlEncode(name)}</td>" +
                          $"<td style=\"padding:3px 8px;border-bottom:1px solid #eee;color:#666\">{WebUtility.HtmlEncode(prov)}</td>" +
                          $"<td style=\"padding:3px 8px;border-bottom:1px solid #eee;text-align:right\">{WebUtility.HtmlEncode(cost)}</td></tr>");
            }
            sb.Append("</table>");
            if (newMpiTotal > newMpis.Count) { sb.Append($"<p style=\"color:#666\">…and {newMpiTotal - newMpis.Count} more.</p>"); }
        }

        // --- Public roster & pre-qual watch (Mondays only) ---------------------
        // 2026-07-06 (Ian): weekly digest of open consultant rosters / pre-qual
        // lists relevant to KOR (structural/buildings) + health-authority RFPQs,
        // so nothing in the BC Bid feed sits unactioned. Rides the daily email.
        if (DateTime.Now.DayOfWeek == DayOfWeek.Monday)
        {
            sb.Append("<h3>Public roster &amp; pre-qual watch <span style=\"color:#666;font-weight:normal\">(open)</span></h3>");
            try
            {
                var rosterRows = new List<(string Buyer, string Name, DateTimeOffset? Due, string? Url)>();
                await using (var cmd = new SqlCommand(@"
SELECT o.BuyerName, o.Name, o.SubmissionDeadlineUtc,
       (SELECT TOP 1 ob.Url FROM opportunities.OpportunityObservations ob WHERE ob.OpportunityId = o.Id AND ob.IsActive = 1) AS Url
FROM opportunities.Opportunities o
WHERE o.SubmissionDeadlineUtc >= sysdatetimeoffset()
  AND ( UPPER(o.Name) LIKE N'%PRE-QUAL%' OR UPPER(o.Name) LIKE N'%PREQUAL%' OR UPPER(o.Name) LIKE N'%RFPQ%' OR UPPER(o.Name) LIKE N'%RFSQ%'
     OR UPPER(o.Name) LIKE N'%ROSTER%' OR UPPER(o.Name) LIKE N'%STANDING%' OR UPPER(o.Name) LIKE N'%QUALIFIED SUPPLIER%'
     OR UPPER(o.Name) LIKE N'%MULTI-USE%' OR UPPER(o.Name) LIKE N'%ON-CALL%' OR UPPER(o.Name) LIKE N'%AS AND WHEN%' )
  AND ( UPPER(o.Name) LIKE N'%ENGINEER%' OR UPPER(o.Name) LIKE N'%CONSULT%' OR UPPER(o.Name) LIKE N'%STRUCTURAL%'
     OR UPPER(o.Name) LIKE N'%ARCHITECT%' OR UPPER(o.Name) LIKE N'%BUILDING%' OR UPPER(o.Name) LIKE N'%FACILIT%'
     OR UPPER(o.Name) LIKE N'%PROFESSIONAL%' OR UPPER(o.BuyerName) LIKE N'%HEALTH%' )
  AND UPPER(o.Name) NOT LIKE N'%CONTRACTOR%' AND UPPER(o.Name) NOT LIKE N'%EQUIPMENT%' AND UPPER(o.Name) NOT LIKE N'%FURNITURE%'
  AND UPPER(o.Name) NOT LIKE N'%FENCE%' AND UPPER(o.Name) NOT LIKE N'%FOREST%' AND UPPER(o.Name) NOT LIKE N'%PAINTING%'
  AND UPPER(o.Name) NOT LIKE N'%WAYFINDING%' AND UPPER(o.Name) NOT LIKE N'%TRAIL%' AND UPPER(o.Name) NOT LIKE N'%PATHWAY%'
  AND UPPER(o.Name) NOT LIKE N'%I.T.%' AND UPPER(o.Name) NOT LIKE N'%CONTRACTING%' AND UPPER(o.Name) NOT LIKE N'%TRANSPORTATION%'
ORDER BY o.SubmissionDeadlineUtc ASC;", con))
                await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await r.ReadAsync(ct).ConfigureAwait(false))
                    {
                        rosterRows.Add((
                            r.IsDBNull(0) ? "" : r.GetString(0),
                            r.IsDBNull(1) ? "" : r.GetString(1),
                            r.IsDBNull(2) ? (DateTimeOffset?)null : r.GetDateTimeOffset(2),
                            r.IsDBNull(3) ? null : r.GetString(3)));
                    }
                }

                if (rosterRows.Count == 0) { sb.Append("<p style=\"color:#666\">None open.</p>"); }
                else
                {
                    sb.Append("<table style=\"border-collapse:collapse;width:100%\">");
                    foreach (var (buyer, name, due, url) in rosterRows)
                    {
                        var soon = due.HasValue && due.Value.ToUniversalTime() <= DateTimeOffset.UtcNow.AddDays(14);
                        var dueTxt = due.HasValue ? due.Value.ToString("yyyy-MM-dd") : "—";
                        var nameHtml = string.IsNullOrWhiteSpace(url)
                            ? WebUtility.HtmlEncode(name)
                            : $"<a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(name)}</a>";
                        var dueStyle = soon ? "color:#C8102E;font-weight:bold" : "color:#666";
                        sb.Append($"<tr><td style=\"padding:3px 8px;border-bottom:1px solid #eee;color:#666\">{WebUtility.HtmlEncode(buyer)}</td>" +
                                  $"<td style=\"padding:3px 8px;border-bottom:1px solid #eee\">{nameHtml}</td>" +
                                  $"<td style=\"padding:3px 8px;border-bottom:1px solid #eee;text-align:right;{dueStyle}\">{WebUtility.HtmlEncode(dueTxt)}</td></tr>");
                    }
                    sb.Append("</table>");
                }
            }
            catch (Exception ex)
            {
                sb.Append($"<p style=\"color:#C8102E\">Roster watch failed: {WebUtility.HtmlEncode(ex.GetType().Name)}: {WebUtility.HtmlEncode(ex.Message)}</p>");
            }
        }

        // --- Verdict movement --------------------------------------------------
        sb.Append("<h3>Verdict movement</h3>");
        var pursueRows = new List<string>();
        var verdictCounts = new List<(string V, int N)>();
        await using (var cmd = new SqlCommand(@"
WITH Fresh AS (
  SELECT m.ProjectName,
         COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),'')) AS V
  FROM opportunities.MajorProjectEnrichment e
  JOIN opportunities.MajorProjectsInventory m ON m.Id = e.MajorProjectsInventoryId
  WHERE e.ProviderName = N'ProjectBriefHoning'
    AND e.LastRefreshAtUtc >= DATEADD(hour, -24, sysdatetimeoffset()))
SELECT V, COUNT(*) AS N,
       STRING_AGG(CAST(CASE WHEN V IN (N'PURSUE', N'PURSUE_URGENT') THEN ProjectName END AS nvarchar(max)), N'; ') AS Names
FROM Fresh WHERE V IS NOT NULL GROUP BY V ORDER BY N DESC;", con))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var v = r.GetString(0);
                verdictCounts.Add((v, r.GetInt32(1)));
                if (!r.IsDBNull(2) && (v == "PURSUE" || v == "PURSUE_URGENT"))
                {
                    pursueRows.Add($"<b>{v}</b>: {WebUtility.HtmlEncode(r.GetString(2))}");
                }
            }
        }

        if (verdictCounts.Count == 0) { sb.Append("<p style=\"color:#666\">No verdicts in the last 24h.</p>"); }
        else
        {
            sb.Append("<p>");
            sb.Append(string.Join(" · ", verdictCounts.ConvertAll(x => $"{x.V} <b>{x.N}</b>")));
            sb.Append("</p>");
            foreach (var line in pursueRows) { sb.Append($"<p style=\"margin:2px 0\">{line}</p>"); }
        }

        // --- Retirements -------------------------------------------------------
        await AppendCountTableAsync(sb, con, "Retired (last 24h)", @"
SELECT LEFT(COALESCE(RetiredReason, N'(no reason)'), 60) AS K, COUNT(*) AS N
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc >= DATEADD(hour, -24, sysdatetimeoffset())
GROUP BY LEFT(COALESCE(RetiredReason, N'(no reason)'), 60) ORDER BY N DESC;", ct).ConfigureAwait(false);

        // --- Ingest activity ---------------------------------------------------
        await AppendCountTableAsync(sb, con, "Research ingested (last 24h)", @"
SELECT K, SUM(N) AS N FROM (
  SELECT CASE e.ProviderName WHEN N'ProjectBrief' THEN N'Project briefs' ELSE N'Project honing' END AS K, COUNT(*) AS N
  FROM opportunities.MajorProjectEnrichment e
  WHERE e.LastRefreshAtUtc >= DATEADD(hour, -24, sysdatetimeoffset()) AND e.ProviderName IN (N'ProjectBrief', N'ProjectBriefHoning')
  GROUP BY e.ProviderName
  UNION ALL
  SELECT CASE WHEN e.ProviderName = N'FirmNarrative' THEN N'Org briefs'
              WHEN e.ProviderName = N'FirmNarrativeHoning' THEN N'Org honing'
              WHEN e.ProviderName LIKE N'PersonBriefHoning-%' THEN N'People honing'
              ELSE N'People briefs' END, COUNT(*)
  FROM opportunities.CanonicalOrgEnrichment e
  WHERE e.LastRefreshAtUtc >= DATEADD(hour, -24, sysdatetimeoffset())
    AND (e.ProviderName IN (N'FirmNarrative', N'FirmNarrativeHoning') OR e.ProviderName LIKE N'PersonBrief%')
  GROUP BY CASE WHEN e.ProviderName = N'FirmNarrative' THEN N'Org briefs'
                WHEN e.ProviderName = N'FirmNarrativeHoning' THEN N'Org honing'
                WHEN e.ProviderName LIKE N'PersonBriefHoning-%' THEN N'People honing'
                ELSE N'People briefs' END
) x GROUP BY K ORDER BY N DESC;", ct).ConfigureAwait(false);

        // --- Failures ----------------------------------------------------------
        await AppendCountTableAsync(sb, con, "Failures / non-Ok enrichment (last 24h)", @"
SELECT K, SUM(N) AS N FROM (
  SELECT N'Project: ' + e.Status AS K, COUNT(*) AS N FROM opportunities.MajorProjectEnrichment e
  WHERE e.UpdatedAtUtc >= DATEADD(hour, -24, sysdatetimeoffset()) AND e.Status <> N'Ok' GROUP BY e.Status
  UNION ALL
  SELECT N'Org/People: ' + e.Status, COUNT(*) FROM opportunities.CanonicalOrgEnrichment e
  WHERE e.UpdatedAtUtc >= DATEADD(hour, -24, sysdatetimeoffset()) AND e.Status <> N'Ok' GROUP BY e.Status
) x GROUP BY K ORDER BY N DESC;", ct).ConfigureAwait(false);

        // --- Coverage tickers --------------------------------------------------
        await using (var cmd = new SqlCommand(@"
SELECT
 (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory WHERE RetiredAtUtc IS NULL),
 (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m WHERE m.RetiredAtUtc IS NULL
   AND EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment e WHERE e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBrief' AND e.ResultJson IS NOT NULL)),
 (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m WHERE m.RetiredAtUtc IS NULL
   AND EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment e WHERE e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
     AND COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),'')) IS NOT NULL)),
 (SELECT COUNT(DISTINCT TRY_CAST(SUBSTRING(e.ProviderName,13,20) AS bigint)) FROM opportunities.CanonicalOrgEnrichment e
   WHERE e.ProviderName LIKE N'PersonBrief-%' AND e.ProviderName NOT LIKE N'PersonBriefHoning-%' AND e.Status = N'Ok' AND e.ResultJson IS NOT NULL),
 (SELECT COUNT(*) FROM opportunities.IntelAction WHERE RetiredAtUtc IS NULL AND Status = N'Open');", con))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            await r.ReadAsync(ct).ConfigureAwait(false);
            var active = r.GetInt32(0);
            sb.Append("<h3>Coverage</h3><p>");
            sb.Append($"Projects: <b>{active}</b> active · briefed <b>{r.GetInt32(1)}</b> ({100 * r.GetInt32(1) / Math.Max(1, active)}%) · honed <b>{r.GetInt32(2)}</b> ({100 * r.GetInt32(2) / Math.Max(1, active)}%) · ");
            sb.Append($"People briefed: <b>{r.GetInt32(3)}</b> · Open actions: <b>{r.GetInt32(4)}</b></p>");
        }

        // --- Latest trigger report (server QueueDrain, 21:30 builder job) ------
        await using (var cmd = new SqlCommand(@"
SELECT TOP 1 RunAtUtc, PendingQueues, ReportText
FROM opportunities.QueueRefreshReport ORDER BY RunAtUtc DESC;", con))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            sb.Append("<h3>Drain queues (last trigger run)</h3>");
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                // 2026-06-12: tightened 20h -> 10h after a dead trigger sat a
                // full day inside the old window. The trigger fires nightly at
                // 21:30; by the 6am email it is ~8.5h old, so >10h means last
                // night's run did not happen.
                var age = DateTimeOffset.UtcNow - r.GetDateTimeOffset(0).ToUniversalTime();
                if (age > TimeSpan.FromHours(10))
                {
                    sb.Append($"<p style=\"color:#C8102E\"><b>STALE:</b> last trigger run was {age.TotalHours:N0}h ago — BdResearchQueueBuilderJob (21:30) did not run; check Worker JobRuns.</p>");
                }

                var pending = r.IsDBNull(1) ? "" : r.GetString(1);
                sb.Append(string.IsNullOrWhiteSpace(pending)
                    ? "<p>All kinds fresh — nothing to run.</p>"
                    : $"<p>Queues with pending work: <b>{WebUtility.HtmlEncode(pending)}</b></p>");
                sb.Append($"<pre style=\"background:#f6f6f6;padding:8px;font-size:12px;overflow:auto\">{WebUtility.HtmlEncode(r.GetString(2))}</pre>");
            }
            else
            {
                sb.Append("<p style=\"color:#C8102E\">No trigger report rows yet.</p>");
            }
        }

        // --- Summary flags (new SUMMARY-*.txt correction lines) ----------------
        // 2026-06-12 process fix: drain sessions file corrections (dups,
        // rebrands, defunct firms, departures) in their SUMMARY files; until
        // the m132/m133 harvest nobody read them. Surface every new summary's
        // flag-pattern lines so nothing files itself silently again.
        AppendSummaryFlags(sb, queueDrainRoot);

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static readonly System.Text.RegularExpressions.Regex FlagPattern = new(
        @"duplicate|merge|rebrand|defunct|bankrupt|receivership|deceased|departed|data error|misclassif|reclassif|dismiss|wrong sector|wrong market|wrong discipline|retire",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void AppendSummaryFlags(StringBuilder sb, string queueDrainRoot)
    {
        const int maxLines = 60;
        sb.Append("<h3>Summary flags (last 24h)</h3>");
        try
        {
            if (!System.IO.Directory.Exists(queueDrainRoot))
            {
                sb.Append($"<p style=\"color:#C8102E\">QueueDrain root not found at {WebUtility.HtmlEncode(queueDrainRoot)}.</p>");
                return;
            }

            var cutoff = DateTime.UtcNow.AddHours(-24);
            var total = 0;
            var anyFile = false;
            foreach (var file in System.IO.Directory.EnumerateFiles(queueDrainRoot, "SUMMARY-*.txt", System.IO.SearchOption.AllDirectories))
            {
                if (System.IO.File.GetLastWriteTimeUtc(file) < cutoff) { continue; }

                var flagged = new List<string>();
                foreach (var line in System.IO.File.ReadLines(file))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0 && FlagPattern.IsMatch(trimmed))
                    {
                        flagged.Add(trimmed.Length > 160 ? trimmed[..160] + "…" : trimmed);
                    }
                }

                if (flagged.Count == 0) { continue; }

                anyFile = true;
                var queue = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(file)) ?? "");
                sb.Append($"<p style=\"margin:6px 0 2px\"><b>{WebUtility.HtmlEncode(queue)}</b> / {WebUtility.HtmlEncode(System.IO.Path.GetFileName(file))} — {flagged.Count} flag line(s)</p>");
                sb.Append("<pre style=\"background:#fff4f4;padding:6px;font-size:11px;overflow:auto;margin:0\">");
                foreach (var line in flagged)
                {
                    if (total >= maxLines)
                    {
                        sb.Append("… (capped — read the file)\n");
                        break;
                    }

                    sb.Append(WebUtility.HtmlEncode(line)).Append('\n');
                    total++;
                }

                sb.Append("</pre>");
                if (total >= maxLines) { break; }
            }

            if (!anyFile)
            {
                sb.Append("<p style=\"color:#666\">No new summaries with flag lines.</p>");
            }
        }
        catch (Exception ex)
        {
            // The flags section must never kill the email.
            sb.Append($"<p style=\"color:#C8102E\">Flags harvest failed: {WebUtility.HtmlEncode(ex.GetType().Name)}: {WebUtility.HtmlEncode(ex.Message)}</p>");
        }
    }

    private static async Task AppendCountTableAsync(StringBuilder sb, SqlConnection con, string title, string sql, System.Threading.CancellationToken ct)
    {
        var rows = new List<(string K, int N)>();
        await using (var cmd = new SqlCommand(sql, con))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add((r.GetString(0), r.GetInt32(1)));
            }
        }

        sb.Append($"<h3>{WebUtility.HtmlEncode(title)}</h3>");
        if (rows.Count == 0) { sb.Append("<p style=\"color:#666\">None.</p>"); return; }
        sb.Append("<table style=\"border-collapse:collapse\">");
        foreach (var (k, n) in rows)
        {
            sb.Append($"<tr><td style=\"padding:2px 12px 2px 0;border-bottom:1px solid #eee\">{WebUtility.HtmlEncode(k)}</td>" +
                      $"<td style=\"padding:2px 0;border-bottom:1px solid #eee;text-align:right\"><b>{n}</b></td></tr>");
        }
        sb.Append("</table>");
    }
}
