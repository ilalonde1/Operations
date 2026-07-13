#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services.Reporting;

/// <summary>
/// Monday-morning Weekly Attack Sheet: the ~25 most pressing open-structural-seat
/// plays, regenerated FRESH from the database at send time and emailed as a PDF.
///
/// Trust rule (Ian, 2026-07-13: "I CANNOT present expired/old data"): the sheet
/// is never a stored artifact — every send re-queries live MPI + contact data,
/// excludes seats marked filled, and stamps each card with the row's last-seen
/// date. Rows not seen by any source within FreshDays are excluded outright.
///
/// PDF via headless Edge on this host; if rendering fails the HTML is attached
/// instead (delivery never silently skips).
/// </summary>
[DisallowConcurrentExecution]
internal sealed class WeeklyAttackSheetJob : IJob
{
    private static readonly string[] EdgePaths =
    {
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    };

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<WeeklyAttackSheetJob> _logger;
    private readonly GraphMailSender _mail;

    public WeeklyAttackSheetJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<WeeklyAttackSheetJob> logger,
        GraphMailSender mail)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mail = mail ?? throw new ArgumentNullException(nameof(mail));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.WeeklyAttackSheetEnabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration", nameof(WeeklyAttackSheetJob));
            return;
        }

        if (string.IsNullOrWhiteSpace(opt.MorningReportTenantId)
            || string.IsNullOrWhiteSpace(opt.MorningReportClientId)
            || string.IsNullOrWhiteSpace(opt.MorningReportClientSecret))
        {
            _logger.LogWarning("{Job}: Graph creds not configured; skipping", nameof(WeeklyAttackSheetJob));
            return;
        }

        var ct = context.CancellationToken;
        var plays = await LoadPlaysAsync(opt.OpportunitiesDb!, opt.WeeklyAttackSheetCount, opt.WeeklyAttackSheetFreshDays, ct).ConfigureAwait(false);
        var contacts = await LoadContactsAsync(opt.OpportunitiesDb!, plays.Select(p => p.ArchitectOrgId).Where(o => o > 0).Distinct().ToArray(), ct).ConfigureAwait(false);
        var week = await LoadWeekActivityAsync(opt.OpportunitiesDb!, ct).ConfigureAwait(false);

        var html = BuildHtml(plays, contacts, week);
        var pdf = TryRenderPdf(html);

        var recipient = string.IsNullOrWhiteSpace(opt.WeeklyAttackSheetRecipient)
            ? opt.MorningReportRecipient
            : opt.WeeklyAttackSheetRecipient;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd");
        var bodyNote =
            $"<p>Attached: this week's attack sheet — the {plays.Count} most pressing open-structural-seat plays, " +
            "regenerated from live data at send time (seats confirmed filled and rows not verified within the freshness window are excluded automatically). " +
            "The full target set lives in the app.</p>";

        await _mail.SendHtmlAsync(
            opt.MorningReportTenantId,
            opt.MorningReportClientId,
            opt.MorningReportClientSecret,
            opt.MorningReportSenderUpn,
            recipient,
            $"KOR Weekly Attack Sheet — {stamp} ({plays.Count} plays)",
            bodyNote,
            attachmentName: pdf is not null ? $"KOR-Weekly-Attack-Sheet-{stamp}.pdf" : $"KOR-Weekly-Attack-Sheet-{stamp}.html",
            attachmentBytes: pdf ?? Encoding.UTF8.GetBytes(html),
            attachmentContentType: pdf is not null ? "application/pdf" : "text/html",
            ct: ct).ConfigureAwait(false);

        var summary = $"sent {plays.Count} plays to {recipient} ({(pdf is not null ? "pdf" : "html-fallback")})";
        _logger.LogInformation("{Job}: {Summary}", nameof(WeeklyAttackSheetJob), summary);
        context.Result = summary;
    }

    internal sealed record Play(
        long Id, string Name, string City, string Prov, string Sector, string Stage,
        string Architect, long ArchitectOrgId, string CostText, string Schedule,
        string Channel, DateTimeOffset LastSeen, int Score);

    internal sealed record Contact(string Name, string Title, string Email, string EmailSource, string Linkedin);

    /// <summary>Last-7-days lifecycle activity for the accountability footer:
    /// "N owned (by whom), N removed" — the sheet is a pointer into the app,
    /// and this line shows the pointer is being followed.</summary>
    internal sealed record WeekActivity(IReadOnlyList<string> Owned, IReadOnlyList<string> Dismissed);

    private static async Task<WeekActivity> LoadWeekActivityAsync(string connStr, CancellationToken ct)
    {
        const string sql = @"
SELECT l.Action, ISNULL(m.ProjectName, N'#' + CAST(l.MpiId AS nvarchar(20))), ISNULL(l.ToStaffId, ISNULL(l.ByStaffId, N'?'))
FROM opportunities.OpportunityAssignmentLog l
LEFT JOIN opportunities.MajorProjectsInventory m ON m.Id = l.MpiId
WHERE l.MpiId IS NOT NULL
  AND l.Action IN (N'MpiOwn', N'MpiDismiss')
  AND l.AtUtc >= DATEADD(DAY, -7, SYSDATETIMEOFFSET())
ORDER BY l.AtUtc DESC;";

        var owned = new List<string>();
        var dismissed = new List<string>();
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var line = $"{r.GetString(1)} ({ShortUpn(r.GetString(2))})";
            if (r.GetString(0) == "MpiOwn") owned.Add(line); else dismissed.Add(line);
        }

        return new WeekActivity(owned, dismissed);
    }

    private static string ShortUpn(string upn)
    {
        var at = upn.IndexOf('@');
        return at > 0 ? upn[..at] : upn;
    }

    private static async Task<List<Play>> LoadPlaysAsync(string connStr, int count, int freshDays, CancellationToken ct)
    {
        // Scoring mirrors the hand-built 2026-07-13 sheet: channel-known first,
        // then sector priority (health > education > civic), then value. The
        // freshness guard is the trust rule: no source has seen the row within
        // the window -> it cannot appear on the sheet.
        // Lifecycle (retired / dismissed / owned / seat filled) lives in
        // vw_ActionableProjects — the ONE actionable predicate (migration 284,
        // doctrine D11). Freshness stays here: it is a per-surface knob.
        const string sql = @"
SELECT TOP (@n)
    m.Id, ISNULL(m.ProjectName,''), ISNULL(m.MunicipalityName,''), ISNULL(m.Province,''),
    ISNULL(m.Sector,''), ISNULL(m.Stage,''), ISNULL(m.ArchitectName,''), ISNULL(m.ArchitectCanonicalOrgId,0),
    ISNULL(m.EstimatedCostText,''), ISNULL(m.ScheduleNotes,''),
    CASE WHEN m.KorPipelineTag LIKE 'SE-channel:%' THEN SUBSTRING(m.KorPipelineTag, 12, 40) ELSE 'unknown' END,
    COALESCE(m.LastVerifiedAtUtc, m.LastSeenAtUtc, m.UpdatedAtUtc),
    (CASE WHEN m.KorPipelineTag LIKE 'SE-channel:%' AND m.KorPipelineTag <> 'SE-channel:unknown' THEN 4 ELSE 0 END
     + CASE WHEN m.Sector LIKE '%ealth%' THEN 3 WHEN m.Sector LIKE '%ducation%' OR m.Sector LIKE '%K-12%' THEN 2
            WHEN m.Sector LIKE '%ivic%' OR m.Sector LIKE '%nstitutional%' OR m.Sector LIKE '%ecreation%' THEN 1 ELSE 0 END
     + CASE WHEN ISNULL(m.ModeledCostCad, ISNULL(m.EstimatedCostCad,0)) >= 100000000 THEN 3
            WHEN ISNULL(m.ModeledCostCad, ISNULL(m.EstimatedCostCad,0)) >= 25000000 THEN 2
            WHEN ISNULL(m.ModeledCostCad, ISNULL(m.EstimatedCostCad,0)) >= 5000000 THEN 1 ELSE 0 END) AS Score
FROM opportunities.vw_ActionableProjects m
WHERE NULLIF(LTRIM(RTRIM(m.ArchitectName)),'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(m.StructuralEngineerName)),'') IS NULL
  AND COALESCE(m.LastVerifiedAtUtc, m.LastSeenAtUtc, m.UpdatedAtUtc) >= DATEADD(DAY, -@fresh, SYSDATETIMEOFFSET())
ORDER BY Score DESC, ISNULL(m.ModeledCostCad, ISNULL(m.EstimatedCostCad,0)) DESC, m.Id;";

        var plays = new List<Play>();
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = Math.Clamp(count, 5, 100);
        cmd.Parameters.Add("@fresh", SqlDbType.Int).Value = Math.Clamp(freshDays, 7, 365);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            plays.Add(new Play(
                Convert.ToInt64(r.GetValue(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.GetString(6), Convert.ToInt64(r.GetValue(7)), r.GetString(8), r.GetString(9), r.GetString(10),
                r.GetDateTimeOffset(11), Convert.ToInt32(r.GetValue(12))));
        }

        return plays;
    }

    private static async Task<Dictionary<long, List<Contact>>> LoadContactsAsync(string connStr, long[] orgIds, CancellationToken ct)
    {
        var map = new Dictionary<long, List<Contact>>();
        if (orgIds.Length == 0) return map;

        var inList = string.Join(",", orgIds); // long ids from our own query — no injection surface
        var sql = $@"
SELECT a.CanonicalOrgId, p.DisplayName, ISNULL(a.Title,''), ISNULL(p.Email,''), ISNULL(p.EmailSource,''), ISNULL(p.LinkedinUrl,'')
FROM opportunities.IntelPersonAffiliation a
JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId AND p.RetiredAtUtc IS NULL
WHERE a.RetiredAtUtc IS NULL AND a.CanonicalOrgId IN ({inList})
ORDER BY a.CanonicalOrgId, CASE WHEN NULLIF(p.Email,'') IS NOT NULL THEN 0 ELSE 1 END, p.Id;";

        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = r.GetInt64(0);
            if (!map.TryGetValue(key, out var list)) { list = new List<Contact>(); map[key] = list; }
            if (list.Count < 3)
            {
                list.Add(new Contact(r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
            }
        }

        return map;
    }

    private static string BuildHtml(IReadOnlyList<Play> plays, Dictionary<long, List<Contact>> contacts, WeekActivity week)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<title>KOR Weekly Attack Sheet</title><style>")
          .Append("body{font-family:system-ui,'Segoe UI',Arial;margin:34px;color:#17212B;font-size:12.5px}")
          .Append("h1,h2{letter-spacing:-.02em;margin:0 0 2px}h2{margin-top:24px;border-bottom:2px solid #3F5364;padding-bottom:4px}")
          .Append(".sub{color:#4B5963;margin-bottom:14px}")
          .Append(".cl{border-collapse:collapse;width:100%;margin-bottom:8px}.cl th{text-align:left;font-size:10px;text-transform:uppercase;letter-spacing:.05em;color:#6E7C86;border-bottom:2px solid #D2D9DE;padding:4px 8px 4px 0}.cl td{border-bottom:1px solid #E1E6EA;padding:6px 8px 6px 0;vertical-align:top}")
          .Append(".card{border:1px solid #D2D9DE;border-left:4px solid #FF5B35;border-radius:8px;padding:11px 14px;margin-bottom:12px;page-break-inside:avoid}")
          .Append(".hd{display:flex;justify-content:space-between;gap:10px;margin-bottom:5px}.pn{font-weight:700;font-size:14px}.meta{color:#6E7C86;font-size:11px;text-align:right}")
          .Append(".row{margin:2px 0}.row b{display:inline-block;width:74px;color:#3F5364;font-size:10.5px;text-transform:uppercase;letter-spacing:.04em}")
          .Append(".ct{margin:1px 0 1px 78px}.t{color:#6E7C86}.e{color:#2C5A7A;font-weight:600}")
          .Append(".play{background:#FFF4F0;border-radius:6px;padding:7px 10px;margin-top:6px}.play b{color:#FF5B35;font-size:10.5px;letter-spacing:.05em;margin-right:6px}")
          .Append(".asof{color:#8a969e;font-size:10px;margin-top:5px}.ch{font-weight:600}")
          .Append("</style>");

        sb.Append("<h1>KOR Weekly Attack Sheet <span style=\"color:#FF5B35\">— wk of ")
          .Append(DateTime.Now.ToString("MMM d, yyyy")).Append("</span></h1>")
          .Append("<div class=sub>The ").Append(plays.Count)
          .Append(" most pressing open-structural-seat plays, regenerated from live data at send time. Seats confirmed filled and rows outside the freshness window are excluded automatically. Full target set lives in the app.</div>");

        // Call list (dedup people across plays).
        sb.Append("<h2>This week&rsquo;s call list</h2><table class=cl><tr><th>Who</th><th>Firm</th><th>Reach</th><th>About</th></tr>");
        var seen = new HashSet<string>();
        foreach (var p in plays)
        {
            if (!contacts.TryGetValue(p.ArchitectOrgId, out var ppl)) continue;
            foreach (var c in ppl)
            {
                var key = c.Name + "|" + p.Architect;
                if (!seen.Add(key)) continue;
                var about = string.Join(" · ", plays.Where(x => x.ArchitectOrgId == p.ArchitectOrgId).Take(3).Select(x => E(x.Name)));
                var reach = !string.IsNullOrWhiteSpace(c.Email)
                    ? $"<span class=e>{E(c.Email)}</span>"
                    : (!string.IsNullOrWhiteSpace(c.Linkedin) ? "LinkedIn" : "firm main line");
                sb.Append($"<tr><td><b>{E(c.Name)}</b><br><span class=t>{E(c.Title)}</span></td><td>{E(p.Architect)}</td><td>{reach}</td><td>{about}</td></tr>");
            }
        }

        sb.Append("</table><h2>The plays</h2>");

        var i = 0;
        foreach (var p in plays)
        {
            i++;
            var playText = p.Channel switch
            {
                "architect-subconsultant" => "Pitch the architect directly — they assemble the consultant team.",
                "design-build" => "Get onto the design-build proponent team — pitch the builder, not the owner.",
                "owner-direct" => "Owner procures the SE separately — watch their portal and pitch the capital-projects lead.",
                _ => "Channel unconfirmed — open with the architect contact and qualify the procurement route.",
            };
            var who = contacts.TryGetValue(p.ArchitectOrgId, out var ppl) && ppl.Count > 0
                ? string.Concat(ppl.Select(c =>
                    $"<div class=ct><b>{E(c.Name)}</b> — {E(c.Title)}" +
                    (string.IsNullOrWhiteSpace(c.Email) ? "" : $" · <span class=e>{E(c.Email)}</span> <span class=t>({E(c.EmailSource)})</span>") +
                    (string.IsNullOrWhiteSpace(c.Linkedin) ? "" : $" · <a href=\"{c.Linkedin}\">LinkedIn</a>") + "</div>"))
                : "<div class=ct><span class=t>no verified contact yet — firm main line</span></div>";

            sb.Append("<div class=card><div class=hd><span class=pn>").Append(i).Append(". ").Append(E(p.Name))
              .Append("</span><span class=meta>").Append(E(p.City)).Append(", ").Append(E(p.Prov)).Append(" · ").Append(E(p.Sector));
            if (!string.IsNullOrWhiteSpace(p.CostText)) sb.Append(" · ").Append(E(p.CostText));
            sb.Append("</span></div>")
              .Append("<div class=row><b>Architect</b> ").Append(E(p.Architect)).Append("</div>")
              .Append("<div class=row><b>Channel</b> <span class=ch>").Append(E(p.Channel)).Append("</span></div>");
            if (!string.IsNullOrWhiteSpace(p.Schedule)) sb.Append("<div class=row><b>Schedule</b> ").Append(E(p.Schedule)).Append("</div>");
            sb.Append("<div class=row><b>Contacts</b></div>").Append(who)
              .Append("<div class=play><b>THE PLAY</b> ").Append(E(playText)).Append("</div>")
              .Append("<div class=asof>row last verified/seen ").Append(p.LastSeen.ToString("yyyy-MM-dd")).Append(" · generated ")
              .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
              .Append(" · <a href=\"kor://mpi/").Append(p.Id).Append("\">open in app</a> — Own it / Not for us there</div></div>");
        }

        // Accountability footer: proof the sheet is being worked. Owned plays
        // drop off next week's sheet automatically (vw_ActionableProjects).
        sb.Append("<h2>Last week&rsquo;s movement</h2><div class=sub>");
        sb.Append(week.Owned.Count == 0
            ? "No plays were taken this week."
            : $"<b>{week.Owned.Count} taken:</b> " + E(string.Join(" · ", week.Owned.Take(10))) + (week.Owned.Count > 10 ? " …" : ""));
        sb.Append("<br>");
        sb.Append(week.Dismissed.Count == 0
            ? "None removed."
            : $"<b>{week.Dismissed.Count} removed (not for us):</b> " + E(string.Join(" · ", week.Dismissed.Take(10))) + (week.Dismissed.Count > 10 ? " …" : ""));
        sb.Append("</div>");

        return sb.ToString();
    }

    private byte[]? TryRenderPdf(string html)
    {
        try
        {
            var edge = EdgePaths.FirstOrDefault(File.Exists);
            if (edge is null) return null;

            var dir = Path.Combine(Path.GetTempPath(), "KorWeeklyAttack");
            Directory.CreateDirectory(dir);
            var htmlPath = Path.Combine(dir, "sheet.html");
            var pdfPath = Path.Combine(dir, "sheet.pdf");
            File.WriteAllText(htmlPath, html, Encoding.UTF8);
            if (File.Exists(pdfPath)) File.Delete(pdfPath);

            var psi = new ProcessStartInfo
            {
                FileName = edge,
                Arguments = $"--headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf=\"{pdfPath}\" \"{new Uri(htmlPath).AbsoluteUri}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null || !proc.WaitForExit(60_000)) { try { proc?.Kill(); } catch { /* best effort */ } return null; }

            return File.Exists(pdfPath) ? File.ReadAllBytes(pdfPath) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Job}: PDF render failed; will attach HTML instead", nameof(WeeklyAttackSheetJob));
            return null;
        }
    }
}
