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
    private readonly ApproachDraftService _approach;

    public WeeklyAttackSheetJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<WeeklyAttackSheetJob> logger,
        GraphMailSender mail,
        ApproachDraftService approach)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mail = mail ?? throw new ArgumentNullException(nameof(mail));
        _approach = approach ?? throw new ArgumentNullException(nameof(approach));
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
        var approaches = await GenerateApproachesAsync(plays, contacts, ct).ConfigureAwait(false);

        var html = BuildHtml(plays, contacts, week, approaches);
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

        var summary = $"sent {plays.Count} plays ({approaches.Count} call-pack blocks) to {recipient} ({(pdf is not null ? "pdf" : "html-fallback")})";
        _logger.LogInformation("{Job}: {Summary}", nameof(WeeklyAttackSheetJob), summary);
        context.Result = summary;
    }

    internal sealed record Play(
        long Id, string Name, string City, string Prov, string Sector, string Stage,
        string Architect, long ArchitectOrgId, string CostText, string Schedule,
        string Channel, DateTimeOffset LastSeen, int Score, string Window);

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
            WHEN ISNULL(m.ModeledCostCad, ISNULL(m.EstimatedCostCad,0)) >= 5000000 THEN 1 ELSE 0 END) AS Score,
    -- Timing is trusted ONLY if it was checked within the freshness window;
    -- an aged 'now' reads as blank here so its badge/rank quietly decay
    -- (SeatTimingRefreshJob re-checks the oldest before they get here).
    CASE WHEN m.SeatWindowCheckedAtUtc >= DATEADD(DAY, -@fresh, SYSDATETIMEOFFSET())
         THEN ISNULL(m.SeatWindow, '') ELSE '' END AS SeatWindow
FROM opportunities.vw_ActionableProjects m
WHERE NULLIF(LTRIM(RTRIM(m.ArchitectName)),'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(m.StructuralEngineerName)),'') IS NULL
  AND COALESCE(m.LastVerifiedAtUtc, m.LastSeenAtUtc, m.UpdatedAtUtc) >= DATEADD(DAY, -@fresh, SYSDATETIMEOFFSET())
-- Urgency FIRST: a freshly-checked 'now — team forming' seat leads any distant
-- one, then channel/sector/value score, then dollar value. Stale-timing rows
-- fall to the neutral middle (rank 1) until re-checked.
ORDER BY CASE WHEN m.SeatWindowCheckedAtUtc >= DATEADD(DAY, -@fresh, SYSDATETIMEOFFSET())
              THEN (CASE m.SeatWindow WHEN 'now' THEN 3 WHEN '2026' THEN 2 WHEN '2027+' THEN 0 ELSE 1 END)
              ELSE 1 END DESC,
         Score DESC, ISNULL(m.ModeledCostCad, ISNULL(m.EstimatedCostCad,0)) DESC, m.Id;";

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
                r.GetDateTimeOffset(11), Convert.ToInt32(r.GetValue(12)), r.GetString(13)));
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

    /// <summary>Drafts each play's Approach block (who to call / script / email)
    /// concurrently (capped). Failures drop out silently — the Call Pack simply
    /// skips that play; the sheet always sends.</summary>
    private async Task<Dictionary<long, string>> GenerateApproachesAsync(
        IReadOnlyList<Play> plays, Dictionary<long, List<Contact>> contacts, CancellationToken ct)
    {
        var result = new Dictionary<long, string>();
        if (!_approach.IsConfigured)
        {
            _logger.LogWarning("{Job}: Anthropic key not configured — Call Pack omitted.", nameof(WeeklyAttackSheetJob));
            return result;
        }

        using var gate = new SemaphoreSlim(5);
        var tasks = plays.Select(async p =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var intel = BuildApproachIntel(p, contacts.TryGetValue(p.ArchitectOrgId, out var c) ? c : new List<Contact>());
                var html = await _approach.DraftHtmlAsync(intel, ct).ConfigureAwait(false);
                return (p.Id, html);
            }
            finally { gate.Release(); }
        });

        foreach (var (id, html) in await Task.WhenAll(tasks).ConfigureAwait(false))
        {
            if (!string.IsNullOrWhiteSpace(html)) result[id] = html!;
        }

        _logger.LogInformation("{Job}: drafted {Count}/{Total} approach blocks.", nameof(WeeklyAttackSheetJob), result.Count, plays.Count);
        return result;
    }

    private static string BuildApproachIntel(Play p, List<Contact> contacts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PROJECT: {p.Name}");
        sb.AppendLine($"Location: {p.City}, {p.Prov}; sector {p.Sector}; stage {p.Stage}; est. cost {p.CostText}");
        sb.AppendLine($"Architect: {p.Architect}");
        sb.AppendLine($"SE procurement channel: {p.Channel}");
        if (!string.IsNullOrWhiteSpace(p.Schedule)) sb.AppendLine($"Schedule/status note: {p.Schedule}");
        sb.AppendLine();
        sb.AppendLine("KNOWN CONTACTS AT THE ARCHITECT (name / title / email):");
        if (contacts.Count == 0) sb.AppendLine("- none on file (use the firm's main line)");
        else foreach (var c in contacts) sb.AppendLine($"- {c.Name} / {c.Title} / {(string.IsNullOrWhiteSpace(c.Email) ? "no email" : c.Email)}");
        sb.AppendLine();
        sb.AppendLine("KOR is the structural engineer seeking this project's structural seat. Draft the approach.");
        return sb.ToString();
    }

    // Punchy tight-grid layout (2026-07-13): small font, two-column cards,
    // one-line play, contacts deduped by firm. Verified against the live sheet
    // before deploy; keep this and the offline sample generator in sync.
    private static string BuildHtml(IReadOnlyList<Play> plays, Dictionary<long, List<Contact>> contacts, WeekActivity week, Dictionary<long, string> approaches)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<title>KOR Attack Sheet</title><style>")
          .Append("*{box-sizing:border-box}")
          .Append("body{font-family:'Segoe UI',system-ui,Arial,sans-serif;color:#16202A;font-size:8.6px;line-height:1.32;margin:0}")
          .Append(".top{display:flex;justify-content:space-between;align-items:baseline;border-bottom:2.5px solid #E1442A;padding-bottom:4px;margin-bottom:8px}")
          .Append(".top h1{font-size:15px;font-weight:800;letter-spacing:-.02em;margin:0}.top h1 span{color:#E1442A}")
          .Append(".top .sub{font-size:8px;color:#5B6B76;text-align:right;max-width:52%}")
          .Append(".grid{column-count:2;column-gap:9px}")
          .Append(".card{break-inside:avoid;border:.7px solid #D7DEE3;border-left:2.5px solid #E1442A;border-radius:4px;padding:5px 7px 4px;margin:0 0 7px;background:#fff}")
          .Append(".hd{display:flex;align-items:baseline;gap:5px}.rk{font-weight:800;color:#E1442A;font-size:9px;min-width:13px}")
          .Append(".pn{font-weight:700;font-size:10px;letter-spacing:-.01em;flex:1;line-height:1.15}")
          .Append(".cost{font-weight:800;font-size:9px;color:#16202A;font-variant-numeric:tabular-nums;white-space:nowrap}")
          .Append(".meta{color:#6E7C86;font-size:7.6px;text-transform:uppercase;letter-spacing:.04em;margin:1px 0 2px}")
          .Append(".arch{font-size:8.8px;margin-bottom:2px}")
          .Append(".chip{color:#fff;font-size:6.6px;font-weight:700;letter-spacing:.05em;padding:1px 5px;border-radius:999px;vertical-align:1px;margin-left:3px}")
          .Append(".now{background:#127A3E;color:#fff;font-size:6.6px;font-weight:800;letter-spacing:.06em;padding:1px 5px;border-radius:999px;margin-left:4px}")
          .Append(".w26{background:#B26A00;color:#fff;font-size:6.6px;font-weight:700;letter-spacing:.06em;padding:1px 5px;border-radius:999px;margin-left:4px}")
          .Append(".ctx{color:#3F5364;font-size:7.8px;margin:1px 0 3px}")
          .Append(".play{background:#FCEDE9;border-radius:3px;padding:3px 6px;margin:2px 0 3px;font-size:8.4px}.play b{color:#C0331C}")
          .Append(".who .c{font-size:8px;margin:1px 0}.who .ti{color:#6E7C86}")
          .Append(".em{color:#1F6F8B;font-weight:700}.noem{color:#93A0A8;font-style:italic}.seealso{color:#8A97A0;font-style:italic;font-size:7.6px}")
          .Append(".asof{color:#A7B1B8;font-size:6.8px;margin-top:2px}.asof a{color:#A7B1B8;text-decoration:none}")
          .Append(".foot{border-top:1px solid #D7DEE3;margin-top:6px;padding-top:5px;font-size:8px;color:#5B6B76}.foot b{color:#16202A}")
          // Call Pack (per-play who-to-call / script / email)
          .Append(".cp{border-top:2.5px solid #E1442A;margin-top:14px;padding-top:8px}")
          .Append(".cph{font-size:13px;font-weight:800;letter-spacing:-.01em;margin-bottom:2px}")
          .Append(".cpsub{font-size:8px;color:#5B6B76;margin-bottom:8px}")
          .Append(".apcard{break-inside:avoid;border:.7px solid #D7DEE3;border-radius:5px;padding:8px 11px;margin:0 0 9px}")
          .Append(".aphd{font-weight:700;font-size:10.5px;margin-bottom:1px}.aphd .rf{color:#E1442A;font-weight:800;margin-right:5px}")
          .Append(".apmeta{color:#6E7C86;font-size:7.6px;text-transform:uppercase;letter-spacing:.04em;margin-bottom:5px}")
          .Append(".aptitle{font-size:7.4px;font-weight:800;letter-spacing:.06em;color:#B0432E;margin:6px 0 2px}")
          .Append(".apc{font-size:9px;margin:1px 0}.apo{font-size:9px;margin:2px 0}.appt{font-size:9px;margin:1px 0 1px 4px}")
          .Append(".apmail{background:#F7F9FA;border-radius:4px;padding:6px 8px;font-size:9px;margin-top:2px;line-height:1.4}")
          .Append(".apsub{margin-bottom:3px}")
          .Append("</style>");

        sb.Append("<div class=top><h1>KOR <span>Attack Sheet</span></h1><div class=sub>wk of ")
          .Append(DateTime.Now.ToString("MMM d, yyyy")).Append(" &middot; ").Append(plays.Count)
          .Append(" live open-SE-seat plays, best-first. Filled / stale / paused auto-excluded. Full set in the app.</div></div>");

        sb.Append("<div class=grid>");
        var seenOrg = new Dictionary<long, int>();
        var i = 0;
        foreach (var p in plays)
        {
            i++;
            var (verb, tail) = PlayLine(p.Channel);
            var (chipTxt, chipCol) = Chip(p.Channel);
            var cost = CleanCost(p.CostText);
            var loc = string.Join(", ", new[] { p.City, p.Prov }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var ctx = FirstSentence(p.Schedule, 150);

            // Contacts deduped by firm: a firm's contacts print once; repeat
            // plays for the same architect point back to save space.
            string who;
            if (p.ArchitectOrgId != 0 && seenOrg.TryGetValue(p.ArchitectOrgId, out var firstNo))
            {
                who = $"<div class=\"c seealso\">&#8627; same firm as play {firstNo} — contacts there</div>";
            }
            else
            {
                var ppl = contacts.TryGetValue(p.ArchitectOrgId, out var list) ? list : new List<Contact>();
                var pick = ppl.Where(c => !string.IsNullOrWhiteSpace(c.Email)).Take(2).ToList();
                if (pick.Count == 0) pick = ppl.Take(1).ToList();
                if (pick.Count > 0)
                {
                    who = string.Concat(pick.Select(c =>
                        "<div class=c><b>" + E(c.Name) + "</b> " +
                        (string.IsNullOrWhiteSpace(c.Title) ? "" : "<span class=ti>" + E(c.Title) + "</span> ") +
                        (string.IsNullOrWhiteSpace(c.Email) ? "<span class=noem>firm main line</span>" : "<span class=em>" + E(c.Email) + "</span>") +
                        "</div>"));
                    if (p.ArchitectOrgId != 0) seenOrg[p.ArchitectOrgId] = i;
                }
                else
                {
                    who = "<div class=\"c noem\">no verified contact yet — firm main line</div>";
                }
            }

            // Urgency flag: 'now — team forming' seats get a green NOW badge so
            // they read at a glance; a 2026 seat gets a muted year badge.
            var urgency = p.Window switch
            {
                "now" => "<span class=now>NOW</span>",
                "2026" => "<span class=w26>2026</span>",
                _ => "",
            };
            sb.Append("<div class=card>")
              .Append("<div class=hd><span class=rk>").Append(i).Append("</span><span class=pn>").Append(E(p.Name)).Append(urgency).Append("</span>");
            if (!string.IsNullOrWhiteSpace(cost)) sb.Append("<span class=cost>").Append(E(cost)).Append("</span>");
            sb.Append("</div>")
              .Append("<div class=meta>").Append(E(loc)).Append(string.IsNullOrWhiteSpace(p.Sector) ? "" : " &middot; " + E(p.Sector)).Append("</div>")
              .Append("<div class=arch><b>").Append(E(p.Architect)).Append("</b> <span class=chip style=\"background:").Append(chipCol).Append("\">").Append(chipTxt).Append("</span></div>");
            if (!string.IsNullOrWhiteSpace(ctx)) sb.Append("<div class=ctx>").Append(E(ctx)).Append("</div>");
            sb.Append("<div class=play><b>").Append(verb).Append("</b> — ").Append(tail).Append("</div>")
              .Append("<div class=who>").Append(who).Append("</div>")
              .Append("<div class=asof><b style=\"color:#3F5364\">REF ").Append(p.Id).Append("</b> &middot; as of ").Append(p.LastSeen.ToString("yyyy-MM-dd"))
              .Append(" &middot; <a href=\"kor://mpi/").Append(p.Id).Append("\">open dossier in app</a></div>")
              .Append("</div>");
        }
        sb.Append("</div>");

        // ---- Call Pack: per-play who-to-call / script / draft email --------
        // The summary above is for scanning; this is what you actually work
        // from. One block per play that has a draft (drafted live at send).
        var withApproach = plays.Where(p => approaches.ContainsKey(p.Id)).ToList();
        if (withApproach.Count > 0)
        {
            sb.Append("<div class=cp><div class=cph>Call Pack</div>")
              .Append("<div class=cpsub>Who to call, what to say, and a draft email for each play — drafted from the current intel at send time. Match by REF #.</div>");
            var rank = 0;
            foreach (var p in plays)
            {
                rank++;
                if (!approaches.TryGetValue(p.Id, out var block)) continue;
                var loc = string.Join(", ", new[] { p.City, p.Prov }.Where(x => !string.IsNullOrWhiteSpace(x)));
                sb.Append("<div class=apcard>")
                  .Append("<div class=aphd><span class=rf>REF ").Append(p.Id).Append("</span>").Append(rank).Append(". ").Append(E(p.Name)).Append("</div>")
                  .Append("<div class=apmeta>").Append(E(p.Architect)).Append(string.IsNullOrWhiteSpace(loc) ? "" : " &middot; " + E(loc)).Append(" &middot; ").Append(E(p.Channel)).Append("</div>")
                  .Append(block)
                  .Append("</div>");
            }
            sb.Append("</div>");
        }

        // Accountability footer: owned plays drop off next week automatically.
        sb.Append("<div class=foot>");
        sb.Append(week.Owned.Count == 0
            ? "No plays taken last week."
            : $"<b>{week.Owned.Count} taken:</b> " + E(string.Join(" · ", week.Owned.Take(10))) + (week.Owned.Count > 10 ? " …" : ""));
        sb.Append(" &nbsp;|&nbsp; ");
        sb.Append(week.Dismissed.Count == 0
            ? "none removed."
            : $"<b>{week.Dismissed.Count} removed:</b> " + E(string.Join(" · ", week.Dismissed.Take(10))) + (week.Dismissed.Count > 10 ? " …" : ""));
        sb.Append("</div>");

        return sb.ToString();
    }

    private static (string Verb, string Tail) PlayLine(string channel) => channel switch
    {
        "architect-subconsultant" => ("Pitch the architect", "attach before they lock the SE sub."),
        "design-build" => ("Pitch the builder", "get on the design-build / GC team, not the owner."),
        "owner-direct" => ("Pitch the owner", "reach the capital-projects lead — SE bought separately."),
        "alliance-captive" => ("Watch early scopes", "alliance / P3 seat likely captive."),
        _ => ("Qualify the route", "open with the architect, confirm how the SE is bought."),
    };

    private static (string Text, string Color) Chip(string channel) => channel switch
    {
        "architect-subconsultant" => ("ARCHITECT-SUB", "#2C6E8F"),
        "design-build" => ("DESIGN-BUILD", "#8A5A00"),
        "owner-direct" => ("OWNER-DIRECT", "#4B7B3F"),
        "alliance-captive" => ("ALLIANCE", "#7A5C7B"),
        _ => ("UNCONFIRMED", "#6E7C86"),
    };

    private static string CleanCost(string? c)
    {
        c ??= "";
        var cur = (c.Contains("USD") || c.Contains("US$")) ? "US$" : "$";
        var m = System.Text.RegularExpressions.Regex.Match(c, @"[\d,]{4,}(?:\.\d+)?");
        if (m.Success && double.TryParse(m.Value.Replace(",", ""), out var val))
        {
            if (val >= 1e9) return cur + (val / 1e9).ToString("0.0") + "B";
            if (val >= 1e6) return cur + (val / 1e6).ToString("0") + "M";
        }
        var mb = System.Text.RegularExpressions.Regex.Match(c, @"\$?\s*([\d.]+)\s*(?:billion|B)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mb.Success && double.TryParse(mb.Groups[1].Value, out var vb)) return cur + vb.ToString("0.0") + "B";
        var mm = System.Text.RegularExpressions.Regex.Match(c, @"\$?\s*([\d.]+)\s*(?:million|M)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mm.Success && double.TryParse(mm.Groups[1].Value, out var vm)) return cur + vm.ToString("0") + "M";
        return "";
    }

    private static string FirstSentence(string? s, int max)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "";
        var parts = System.Text.RegularExpressions.Regex.Split(s, @"(?<=[.;])\s");
        var first = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : s;
        return first.Length > max ? first[..max] + "…" : first;
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
