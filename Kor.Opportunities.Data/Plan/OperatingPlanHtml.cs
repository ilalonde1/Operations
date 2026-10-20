#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Kor.Opportunities.Data.Plan;

/// <summary>
/// The operating plan as ONE page: the Director's Plan tab shows it in a WebView2, and Print plan prints this same page to PDF,
/// so the app and the PDF cannot disagree (the BD Reports pattern: one model, the preview is the document). On screen one view
/// shows at a time (process map, strategy, rhythm, platform) and a stage's panel opens on click; printed, all four views run
/// in order and every stage's panel prints as a card. kor:// links are handed to the app (KorDeepLink): kor://pursuit/{id}
/// opens the pursuit, kor://plan/print prints, kor://plan/decide/{goalId} records Ian's decision on a goal.
/// </summary>
public static class OperatingPlanHtml
{
    public const string PrintLink = "kor://plan/print";
    public const string DecidePrefix = "kor://plan/decide/";
    public const int ItemsPerStage = 6;
    public const int PrintedItems = 3;

    public static string Render(OperatingPlanModel plan, PlanReadings readings, IReadOnlyList<PlanItem> items, DateTimeOffset now)
    {
        var sb = new StringBuilder(64 * 1024);
        var decided = plan.Goals.Count(g => g.Status == "Decided");
        var asOf = readings.ScoreboardAsOf;
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>KOR Operating Plan</title><style>").Append(Css).Append("</style></head>");
        sb.Append("<body data-view=\"map\">");

        // Toolbar: screen only.
        sb.Append("<header class=\"bar screen\"><div><h1>Operating plan</h1><p class=\"meta\">Live · ")
          .Append(asOf is { } d ? "scoreboard readings of " + Day(d) : "no scoreboard reading yet")
          .Append(" · live CRM and radar counts at ").Append(E(now.ToString("HH:mm", CultureInfo.InvariantCulture)))
          .Append(" · <b>").Append(plan.Goals.Count).Append(" goals</b>: ").Append(decided).Append(" decided by you, ")
          .Append(plan.Goals.Count - decided).Append(" recommended</p></div>")
          .Append("<nav class=\"seg\">")
          .Append(Seg("map", "Process map", true)).Append(Seg("strategy", "Strategy", false))
          .Append(Seg("rhythm", "Rhythm", false)).Append(Seg("platform", "Platform", false))
          .Append("</nav><a class=\"btn dark\" href=\"").Append(PrintLink).Append("\">Print plan (PDF)</a></header>");

        // Print cover: print only.
        sb.Append("<header class=\"cover print\"><p class=\"eyebrow\">KOR Structural · Operating plan · ")
          .Append(E(Day(DateOnly.FromDateTime(now.Date)))).Append("</p><h1>The operating plan</h1><p class=\"lede\">")
          .Append("Run KOR <b>like a Fortune 500 company</b> in business development, financials, operations and IT, on tools built for it. ")
          .Append("Live: every number below was read from Deltek, the CRM and NetworkOps when this was printed. ")
          .Append(decided).Append(" of ").Append(plan.Goals.Count).Append(" goals decided; the rest are recommendations.</p></header>");

        MapView(sb, plan, readings, items);
        StrategyView(sb, plan, readings);
        RhythmView(sb, plan, readings);
        PlatformView(sb, plan, readings);

        sb.Append("<footer class=\"src\">Readings: the scoreboard").Append(asOf is { } a ? " of " + Day(a) : "")
          .Append(" (the Financials engine, the CRM and NetworkOps, recorded by the morning brief) and live CRM and radar counts. ")
          .Append("Win/loss figures since 2020 come from the 18 October 2026 review of the fee tracker and the Management share. ")
          .Append("A dash means no reading yet.</footer>");
        sb.Append("<script>").Append(Script).Append("</script></body></html>");
        return sb.ToString();
    }

    private static void MapView(StringBuilder sb, OperatingPlanModel plan, PlanReadings readings, IReadOnlyList<PlanItem> items)
    {
        sb.Append("<section class=\"view\" id=\"v-map\"><h2 class=\"print\">The whole process: signal to cash</h2>");
        sb.Append("<div class=\"mapwrap\"><div class=\"map\">");
        Lane(sb, "Business development · how much work comes in", plan, readings, items, new[] { 1, 2, 3, 4 }, "→");
        sb.Append("<div class=\"turn\">↓</div>");
        Lane(sb, "Operations · how much of it becomes cash", plan, readings, items, new[] { 8, 7, 6, 5 }, "←");
        Lane(sb, "IT · underneath every stage", plan, readings, items, new[] { 9 }, "");
        GoalTable(sb, plan, readings);
        sb.Append("</div><aside class=\"panels\">");
        foreach (var s in plan.Stages) Panel(sb, s, plan, readings, items);
        sb.Append("</aside></div></section>");
    }

    private static void Lane(StringBuilder sb, string label, OperatingPlanModel plan, PlanReadings readings, IReadOnlyList<PlanItem> items,
        int[] order, string arrow)
    {
        sb.Append("<p class=\"lane\">").Append(E(label)).Append("</p><div class=\"row").Append(order.Length == 1 ? " wide" : "").Append("\">");
        for (var i = 0; i < order.Length; i++)
        {
            var s = plan.Stages.FirstOrDefault(x => x.No == order[i]);
            if (s is null) continue;
            if (i > 0) sb.Append("<span class=\"arrow\">").Append(arrow).Append("</span>");
            var count = items.Count(it => it.StageNo == s.No);
            var goals = plan.Goals.Count(g => g.StageNo == s.No);
            sb.Append("<a class=\"node ").Append(s.Pillar == "BD" ? "bd" : "ops").Append(s.No == 2 ? " sel" : "")
              .Append("\" data-stage=\"").Append(s.No).Append("\" href=\"#\" onclick=\"stage(").Append(s.No).Append(");return false;\">")
              .Append("<span class=\"n\">").Append(s.No == 9 ? "IT" : s.No.ToString(CultureInfo.InvariantCulture)).Append("</span>")
              .Append("<span class=\"nm\">").Append(E(s.Name)).Append("</span>")
              .Append("<span class=\"ow\">").Append(E(s.Owner)).Append("</span>")
              .Append("<span class=\"td\">").Append(E(readings.Fill(s.Today))).Append("</span>")
              .Append("<span class=\"ft\"><span><i class=\"dot ").Append(HealthClass(s.Health)).Append("\"></i>")
              .Append(count == 0 ? "nothing due" : count + (s.No >= 6 ? " in today's hour" : " due this week"))
              .Append("</span><span>").Append(goals).Append(goals == 1 ? " goal" : " goals").Append("</span></span></a>");
        }
        sb.Append("</div>");
    }

    private static void Panel(StringBuilder sb, PlanStageRow s, OperatingPlanModel plan, PlanReadings readings, IReadOnlyList<PlanItem> items)
    {
        sb.Append("<div class=\"panel").Append(s.No == 2 ? " on" : "").Append("\" data-stage=\"").Append(s.No).Append("\">");
        sb.Append("<h3>").Append(s.No == 9 ? "IT" : s.No.ToString(CultureInfo.InvariantCulture)).Append(" · ").Append(E(s.Name)).Append("</h3>");
        sb.Append("<p class=\"meta\">").Append(E(s.Owner)).Append("</p>");
        sb.Append("<p class=\"sec\">What happens</p><p>").Append(E(s.What)).Append("</p>");
        sb.Append("<p class=\"sec\">Today</p><p>").Append(E(readings.Fill(s.Today))).Append("</p>");
        var goals = plan.Goals.Where(g => g.StageNo == s.No).ToList();
        if (goals.Count > 0)
        {
            sb.Append("<p class=\"sec\">The goals it serves</p>");
            foreach (var g in goals)
                sb.Append("<div class=\"chain\"><b>").Append(E(g.Title)).Append("</b> · now ").Append(E(Now(g, readings)))
                  .Append(", target ").Append(E(Target(g, readings))).Append(" · ").Append(E(g.Status.ToLowerInvariant())).Append("</div>");
        }
        var mine = items.Where(i => i.StageNo == s.No).ToList();
        sb.Append("<p class=\"sec\">").Append(s.No >= 6 ? "In today's hour" : "Due this week").Append(" · ").Append(mine.Count).Append("</p>");
        if (mine.Count == 0) sb.Append("<p class=\"muted\">Nothing due.</p>");
        var shown = 0;
        foreach (var it in mine.Take(ItemsPerStage))
        {
            // The printed card keeps the first three; the screen panel shows up to ItemsPerStage.
            sb.Append("<div class=\"item").Append(it.Urgent ? " urg" : "").Append(++shown > PrintedItems ? " screen" : "").Append("\">")
              .Append(it.Urgent ? "<b class=\"u\">URGENT</b> · " : "");
            if (it.Link is { } link) sb.Append("<a href=\"").Append(E(link)).Append("\">").Append(E(it.Title)).Append("</a>");
            else sb.Append(E(it.Title));
            if (!string.IsNullOrWhiteSpace(it.Detail)) sb.Append("<span class=\"w\">").Append(E(it.Detail)).Append("</span>");
            sb.Append("</div>");
        }
        if (mine.Count > ItemsPerStage) sb.Append("<p class=\"muted\">").Append(mine.Count - ItemsPerStage).Append(" more in the roll-ups.</p>");
        var rhythm = plan.Rhythm.Where(r => r.StageFrom <= s.No && s.No <= r.StageTo).ToList();
        if (rhythm.Count > 0)
        {
            // Screen only: the printed plan carries the rhythm as its own view, so each card does not repeat it.
            sb.Append("<div class=\"screen\"><p class=\"sec\">Where it is discussed</p>");
            foreach (var r in rhythm) sb.Append("<div class=\"item\"><b>").Append(E(r.Name)).Append("</b><span class=\"w\">").Append(E(readings.Fill(r.WhoWhen))).Append("</span></div>");
            sb.Append("</div>");
        }
        sb.Append("<p class=\"sec\">The system behind it</p><p>").Append(E(readings.Fill(s.SystemText))).Append("</p>");
        sb.Append("<p class=\"sec\">Weak point</p><p class=\"weak\">").Append(E(readings.Fill(s.WeakPoint))).Append("</p></div>");
    }

    private static void GoalTable(StringBuilder sb, OperatingPlanModel plan, PlanReadings readings)
    {
        sb.Append("<table class=\"goals\"><thead><tr><th>Goal</th><th>Stage</th><th>Baseline</th><th>Now</th><th>Target</th><th>Status</th></tr></thead><tbody>");
        foreach (var g in plan.Goals)
        {
            var change = g.Metric is null ? null : readings.Change(g.Metric);
            sb.Append("<tr><td>").Append(E(g.Title)).Append("</td><td>").Append(g.StageNo == 9 ? "IT" : g.StageNo.ToString(CultureInfo.InvariantCulture))
              .Append("</td><td>").Append(E(Baseline(g, readings))).Append("</td><td>").Append(E(Now(g, readings)));
            if (change is { } c) sb.Append(" <span class=\"").Append(c.Better ? "up" : "dn").Append("\">").Append(E(c.Text)).Append("</span>");
            sb.Append("</td><td>").Append(E(Target(g, readings))).Append(" <span class=\"muted\">by ").Append(E(Day(g.DueDate))).Append("</span></td><td>");
            if (g.Status == "Decided")
                sb.Append("<span class=\"st dec\">Decided</span>");
            else
                sb.Append("<span class=\"st rec\">Recommended</span> <a class=\"screen decide\" href=\"").Append(DecidePrefix)
                  .Append(g.Id.ToString(CultureInfo.InvariantCulture)).Append("\">Decide</a>");
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void StrategyView(StringBuilder sb, OperatingPlanModel plan, PlanReadings readings)
    {
        sb.Append("<section class=\"view\" id=\"v-strategy\"><h2>Strategy on a page</h2>");
        sb.Append("<p class=\"lede\">Where KOR should play, how it wins, what Operations must fix and what IT must hold. Every choice is a recommendation until you and the partners decide it.</p>");
        sb.Append("<div class=\"facts\">")
          .Append(Fact(readings.Show("InvoicedMonthAvg6") + " / month", "invoiced over the last six months, against " + readings.Show("InvoicedMonthAvgPrior6") + " the six before."))
          .Append(Fact("43%", "of proposals sent since 2020 were won: 72% of decided ones, but 266 were never closed out."))
          .Append(Fact(readings.Show("ArOver90"), "owed over 90 days, of " + readings.Show("ArTotal") + " owed in all; " + readings.Show("Dso") + " days to collect."))
          .Append(Fact(readings.Show("PursuitsWithFee") + " of " + readings.Show("PursuitsLive"), "live pursuits carry a fee estimate, so that is what the pipeline can be valued on."))
          .Append("</div><div class=\"cols\">");
        Column(sb, plan, readings, "BD", "Business development", "Win more of the right work, earlier", new[] { ("WhereToPlay", "Where to play"), ("HowWeWin", "How KOR wins") });
        Column(sb, plan, readings, "Ops", "Operations", "Turn work into cash, and stop giving hours away", new[] { ("WhereItHurts", "Where it hurts"), ("Priorities", "Priorities") });
        Column(sb, plan, readings, "IT", "IT", "Every machine healthy, patched and backed up, on tools KOR owns", new[] { ("Priorities", "Priorities") });
        sb.Append("</div><h3>What you and the partners decide</h3><ul class=\"plain\">");
        foreach (var p in plan.Points.Where(p => p.Section == "Decide"))
            sb.Append("<li><b>").Append(E(readings.Fill(p.Title))).Append("</b> ").Append(E(readings.Fill(p.Detail))).Append("</li>");
        sb.Append("</ul></section>");
    }

    private static void Column(StringBuilder sb, OperatingPlanModel plan, PlanReadings readings, string pillar, string label, string headline,
        (string Section, string Heading)[] sections)
    {
        sb.Append("<div class=\"col\"><p class=\"rec\">").Append(E(label)).Append("</p><h3>").Append(E(headline)).Append("</h3>");
        foreach (var (section, heading) in sections)
        {
            var points = plan.Points.Where(p => p.Pillar == pillar && p.Section == section).ToList();
            if (points.Count == 0) continue;
            sb.Append("<h4>").Append(E(heading)).Append("</h4><ul>");
            foreach (var p in points) sb.Append("<li><b>").Append(E(readings.Fill(p.Title))).Append("</b> ").Append(E(readings.Fill(p.Detail))).Append("</li>");
            sb.Append("</ul>");
        }
        var goals = plan.Goals.Where(g => g.Pillar == pillar).ToList();
        if (goals.Count > 0)
        {
            sb.Append("<h4>Goals to ").Append(E(Day(goals.Max(g => g.DueDate)))).Append("</h4><ul>");
            foreach (var g in goals)
                sb.Append("<li>").Append(E(g.Title)).Append(" <span class=\"muted\">· now ").Append(E(Now(g, readings))).Append(" · ")
                  .Append(E(g.Status.ToLowerInvariant())).Append("</span></li>");
            sb.Append("</ul>");
        }
        sb.Append("</div>");
    }

    private static void RhythmView(StringBuilder sb, OperatingPlanModel plan, PlanReadings readings)
    {
        sb.Append("<section class=\"view\" id=\"v-rhythm\"><h2>The rhythm</h2>");
        sb.Append("<p class=\"lede\">The daily hour, the weekly roll-ups and the meetings, each tied to a goal and to the stages it covers, so every task traces back to the plan.</p>");
        sb.Append("<table class=\"rhythm\"><thead><tr><th>Rhythm</th><th>Who · when</th><th>What happens</th><th>Moves</th><th>Stages</th></tr></thead><tbody>");
        foreach (var r in plan.Rhythm)
            sb.Append("<tr><td><b>").Append(E(r.Name)).Append("</b></td><td>").Append(E(readings.Fill(r.WhoWhen))).Append("</td><td>")
              .Append(E(readings.Fill(r.WhatHappens))).Append("</td><td>").Append(E(readings.Fill(r.Moves))).Append("</td><td>")
              .Append(StageSpan(r.StageFrom, r.StageTo)).Append("</td></tr>");
        sb.Append("</tbody></table><div class=\"box\"><p class=\"sec\">The scoreboard is the thread</p><p>The scoreboard records the money and pipeline numbers every morning. The daily hour reads it, each monthly meets with its change, and the quarterly review judges the plan by it. A task that moves none of its numbers should not be in the rhythm.</p></div></section>");
    }

    private static void PlatformView(StringBuilder sb, OperatingPlanModel plan, PlanReadings readings)
    {
        sb.Append("<section class=\"view\" id=\"v-platform\"><h2>The platform</h2>");
        sb.Append("<p class=\"lede\">The tools being built so a firm of about sixty runs like a Fortune 500 company. The system watches, prepares and drafts; people decide and act.</p>");
        sb.Append("<div class=\"steps\"><div><b>Watch</b>Every source, every night: tenders, permits, Deltek, the CRM, every PC and server.</div>")
          .Append("<div><b>Prepare</b>Ranked, with the evidence and KOR's own record behind each item.</div>")
          .Append("<div><b>Draft</b>The email, the agenda, the roll-up, ready to send.</div>")
          .Append("<div><b>Decide</b>A person reads it and acts. Nothing goes out on its own.</div></div><div class=\"pillars\">");
        foreach (var p in plan.Pillars)
            sb.Append("<div class=\"pillar\"><h3>").Append(E(p.Title)).Append("</h3><dl>")
              .Append("<dt>Fortune 500 has</dt><dd>").Append(E(readings.Fill(p.Fortune500))).Append("</dd>")
              .Append("<dt>KOR has</dt><dd>").Append(E(readings.Fill(p.KorHas))).Append("</dd>")
              .Append("<dt>Found</dt><dd>").Append(E(readings.Fill(p.Found))).Append("</dd>")
              .Append("<dt>Next</dt><dd class=\"weak\">").Append(E(readings.Fill(p.NextText))).Append("</dd></dl></div>");
        sb.Append("</div></section>");
    }

    /// <summary>The file name Print plan offers: KOR-Operating-Plan-yyyy-MM-dd.pdf, dated the day it was printed.</summary>
    public static string PdfFileName(DateTimeOffset now) =>
        "KOR-Operating-Plan-" + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".pdf";

    /// <summary>Parses kor://plan/decide/{goalId}.</summary>
    public static bool TryParseDecide(string? uri, out int goalId)
    {
        goalId = 0;
        return uri is not null && uri.StartsWith(DecidePrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(uri.AsSpan(DecidePrefix.Length).TrimEnd('/'), NumberStyles.None, CultureInfo.InvariantCulture, out goalId);
    }

    // ----- values -----

    public static string Now(PlanGoalRow g, PlanReadings readings) => g.Metric is null ? PlanReadings.Dash : readings.Show(g.Metric);

    public static string Baseline(PlanGoalRow g, PlanReadings readings)
    {
        var m = PlanMetrics.Find(g.Metric);
        if (m is null) return PlanReadings.Dash;
        if (g.BaselineValue is { } b) return PlanMetrics.Format(m.Unit, b);
        return readings[g.Metric]?.Baseline is { } rb ? PlanMetrics.Format(m.Unit, rb) : PlanReadings.Dash;
    }

    public static string Target(PlanGoalRow g, PlanReadings readings)
    {
        var m = PlanMetrics.Find(g.Metric);
        return m is not null && g.TargetValue is { } t ? PlanMetrics.Format(m.Unit, t) : readings.Fill(g.TargetText);
    }

    private static string StageSpan(int from, int to) => from == to ? Stage(from) : from == 1 && to == 9 ? "All" : Stage(from) + "–" + Stage(to);

    private static string Stage(int n) => n == 9 ? "IT" : n.ToString(CultureInfo.InvariantCulture);

    private static string HealthClass(string health) => health switch { "Good" => "g", "Broken" => "r", _ => "a" };

    private static string Day(DateOnly d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string Seg(string view, string label, bool on) =>
        $"<a href=\"#\" data-view=\"{view}\" class=\"{(on ? "on" : "")}\" onclick=\"show('{view}');return false;\">{E(label)}</a>";

    private static string Fact(string big, string text) => "<div class=\"fact\"><b>" + E(big) + "</b><span>" + E(text) + "</span></div>";

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private const string Script = @"
function show(v){document.body.dataset.view=v;document.querySelectorAll('.seg a').forEach(function(a){a.classList.toggle('on',a.dataset.view===v);});window.scrollTo(0,0);}
function stage(n){document.querySelectorAll('.node').forEach(function(e){e.classList.toggle('sel',e.dataset.stage==n);});document.querySelectorAll('.panel').forEach(function(e){e.classList.toggle('on',e.dataset.stage==n);});}";

    private const string Css = @"
:root{--ink:#17212c;--soft:#5A6876;--faint:#8A97A2;--line:#d3d9df;--line2:#eef1f4;--slate:#2C3A47;--slate2:#3F5364;--accent:#FF5B35;--accent-ink:#C6360F;--good:#1E7D3A;--crit:#b3261e;}
*{box-sizing:border-box}
body{margin:0;padding:14px 18px 30px;font-family:'Segoe UI',Arial,sans-serif;color:var(--ink);background:#fff;font-size:13px;line-height:1.45}
a{color:#2f6db5;text-decoration:none}
h1{font-size:22px;margin:0;font-weight:600} h2{font-size:20px;margin:4px 0 6px;font-weight:600} h3{font-size:15px;margin:10px 0 6px;font-weight:600}
.meta{color:var(--soft);font-size:12px;margin:2px 0 0} .meta b{color:var(--ink)} .muted{color:var(--faint);font-size:11.5px}
.bar{display:flex;align-items:center;gap:14px;margin-bottom:12px} .bar>div{flex:1}
.seg a{display:inline-block;border:1px solid var(--line);padding:5px 12px;font-size:12px;margin-right:-1px;color:var(--ink);background:#fff}
.seg a.on{background:var(--slate);color:#fff;border-color:var(--slate)}
.btn{border:1px solid var(--line);padding:5px 12px;font-size:12px;color:var(--ink);background:#fff} .btn.dark{background:var(--slate);color:#fff;border-color:var(--slate)}
.print{display:none}
.view{display:none} body[data-view=map] #v-map,body[data-view=strategy] #v-strategy,body[data-view=rhythm] #v-rhythm,body[data-view=platform] #v-platform{display:block}
.mapwrap{display:grid;grid-template-columns:minmax(0,1fr) 400px;gap:16px;align-items:start}
.lane{font-family:Consolas,monospace;font-size:10.5px;letter-spacing:.12em;color:var(--faint);margin:8px 0 6px;text-transform:uppercase}
.row{display:flex;align-items:stretch} .row .node{flex:1;min-width:0} .arrow{width:22px;display:flex;align-items:center;justify-content:center;color:var(--faint);font-size:15px}
.node{display:flex;flex-direction:column;border:1px solid var(--line);border-radius:8px;padding:8px 10px;background:#fff;color:var(--ink)}
.node:hover{border-color:#9fb0c0} .node.sel{border:2px solid var(--accent);box-shadow:0 0 0 3px rgba(255,91,53,.15);padding:7px 9px}
.node .n{align-self:flex-start;font-family:Consolas,monospace;font-size:10px;color:#fff;background:var(--accent);border-radius:4px;padding:1px 5px}
.node.ops .n{background:var(--slate2)} .node .nm{font-weight:600;font-size:14px;margin:3px 0 1px}
.node .ow{color:var(--soft);font-size:11px;display:-webkit-box;-webkit-line-clamp:1;-webkit-box-orient:vertical;overflow:hidden}
.node .td{font-size:12px;margin:5px 0 0;flex:1}
.row.wide .td{display:inline}
.node .ft{display:flex;justify-content:space-between;gap:6px;margin-top:7px;font-size:11px;color:var(--soft);border-top:1px solid var(--line2);padding-top:5px}
.dot{display:inline-block;width:8px;height:8px;border-radius:50%;margin-right:4px} .dot.g{background:#2e8b57} .dot.a{background:#e5a800} .dot.r{background:#c0392b}
.turn{text-align:right;color:var(--faint);margin:2px 12% 0 0}
table{border-collapse:collapse;width:100%;font-size:12px;margin-top:14px} th{text-align:left;font-family:Consolas,monospace;font-size:10px;letter-spacing:.1em;color:var(--faint);font-weight:400;text-transform:uppercase;border-bottom:1px solid var(--line);padding:5px 6px} td{border-bottom:1px solid var(--line2);padding:6px;vertical-align:top}
.up{color:var(--good);font-size:11px} .dn{color:var(--crit);font-size:11px}
.st{font-size:10.5px;border-radius:10px;padding:1px 7px;white-space:nowrap} .st.rec{background:#FBF1DE;color:#8a5d0d} .st.dec{background:#E7F3EB;color:var(--good)}
.decide{font-size:11px;margin-left:4px}
.panels{border:1px solid var(--line);border-radius:8px;padding:12px 14px;position:sticky;top:8px}
.panel{display:none} .panel.on{display:block} .panel h3{font-size:17px;margin:0}
.sec{font-family:Consolas,monospace;font-size:10.5px;letter-spacing:.12em;color:var(--faint);text-transform:uppercase;margin:11px 0 4px}
.panel p{margin:0} .chain{font-size:12px;background:#F4F2F0;border-radius:6px;padding:6px 8px;margin:4px 0} .chain b{color:var(--accent-ink)}
.item{border-left:3px solid var(--line);padding:3px 0 3px 8px;margin:5px 0;font-size:12px} .item.urg{border-left-color:var(--crit)} .item .w{display:block;color:var(--soft);font-size:11px}
.u{color:var(--crit)} .weak{color:var(--crit)}
.lede{color:var(--soft);margin:0 0 10px;max-width:60rem}
.facts{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:8px 0 14px} .fact{border:1px solid var(--line);border-left:4px solid var(--accent);border-radius:8px;padding:8px 10px} .fact b{display:block;font-size:18px} .fact span{color:var(--soft);font-size:12px}
.cols{display:grid;grid-template-columns:repeat(3,1fr);gap:12px} .col{border:1px solid var(--line);border-radius:8px;padding:10px 12px}
.rec{font-family:Consolas,monospace;font-size:10.5px;letter-spacing:.12em;text-transform:uppercase;color:var(--accent-ink);margin:0} .col h3{margin:2px 0 6px}
h4{margin:10px 0 3px;font-size:10.5px;letter-spacing:.12em;text-transform:uppercase;color:var(--soft);font-family:Consolas,monospace;font-weight:400}
.col ul{margin:2px 0;padding-left:16px} .col li{margin:3px 0}
ul.plain{padding-left:18px} ul.plain li{margin:4px 0}
.box{border-left:3px solid var(--slate2);background:#F4F2F0;border-radius:8px;padding:8px 12px;margin-top:12px} .box p{margin:2px 0}
.steps{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin:6px 0 12px} .steps div{border:1px solid var(--line);border-top:3px solid var(--accent);border-radius:8px;padding:7px 9px;font-size:12px} .steps b{display:block;font-family:Consolas,monospace;font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;color:var(--accent-ink)}
.pillars{display:grid;grid-template-columns:1fr 1fr;gap:10px} .pillar{border:1px solid var(--line);border-radius:8px;padding:9px 12px} .pillar h3{margin:0 0 5px}
.pillar dl{margin:0;display:grid;grid-template-columns:7.5rem 1fr;gap:3px 8px;font-size:12px} .pillar dt{font-family:Consolas,monospace;font-size:10px;letter-spacing:.08em;text-transform:uppercase;color:var(--faint);padding-top:2px} .pillar dd{margin:0}
.src{color:var(--faint);font-size:11px;margin-top:18px;border-top:1px solid var(--line2);padding-top:6px}
.cover{background:linear-gradient(180deg,#2C3A47,#3F5364);color:#fff;padding:16px 18px;border-bottom:3px solid var(--accent);margin:0 0 10px}
.cover .eyebrow{font-family:Consolas,monospace;font-size:10px;letter-spacing:.2em;text-transform:uppercase;color:#FFB59F;margin:0 0 6px} .cover h1{font-size:28px;color:#fff} .cover .lede{color:#D4E0EA;margin:6px 0 0}
@media print{
 @page{size:letter;margin:11mm 11mm}
 body{padding:0;font-size:8.5pt;line-height:1.35;-webkit-print-color-adjust:exact;print-color-adjust:exact}
 .screen{display:none!important} .print{display:block}
 .view{display:block!important;break-before:page} #v-map{break-before:auto}
 .mapwrap{display:block} .panels{border:0;padding:0;position:static;display:grid;grid-template-columns:1fr 1fr 1fr;gap:6px;margin-top:10px}
 .panel{display:block!important;border:1px solid var(--line);border-radius:8px;padding:6px 8px;break-inside:avoid;font-size:7.6pt}
 .panel h3{font-size:10.5pt} .panel .sec{margin:6px 0 2px;font-size:6.8pt} .panel .item{margin:2px 0;font-size:7.6pt} .panel .item .w{display:none}
 .panel .chain{padding:3px 6px;font-size:7.6pt} .src{margin-top:8px}
 .node.sel{border:1px solid var(--line);box-shadow:none;padding:8px 10px}
 .facts{grid-template-columns:repeat(4,1fr)} .cols{grid-template-columns:repeat(3,1fr)} .col,.pillar,.fact,tr{break-inside:avoid}
}";
}
