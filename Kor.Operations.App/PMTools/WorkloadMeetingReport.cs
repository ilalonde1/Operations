#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Kor.Operations.App.PMTools;

/// <summary>
/// PM Tools spec R17 (Jim 13 Mar 1:04:10): "at the end of the meeting, we could just press a button
/// and send out the final report to everybody that identifies all the jobs that we're going to be
/// working on and who's committed to work on them". Builds that report as an HTML email from the
/// meeting's prioritised (P1-P5) jobs - the same rows the top list shows, independent of the grid
/// filters - and the recipients (each job's PM and drafting manager). It is opened as an UNSENT
/// Outlook draft (OutlookDraft.OpenHtml): Jim reviews it and presses Send.
/// Tables with inline styles only, because Outlook ignores style sheets and most CSS.
/// </summary>
internal static class WorkloadMeetingReport
{
    /// <summary>One prioritised job as the report needs it.</summary>
    internal sealed record Job(
        string Wbs1, string Name, int Priority, string Pm, string DraftingManager,
        DateTime? DueDate, string DueWhat, string Notes);

    private const string Font = "font-family:Segoe UI,Arial,sans-serif;font-size:10.5pt;color:#1F2937;";
    private const string Th = "text-align:left;padding:6px 10px;background:#F3F4F6;border-bottom:1px solid #D1D5DB;font-size:9.5pt;color:#4B5563;font-weight:600;";
    private const string Td = "padding:6px 10px;border-bottom:1px solid #E5E7EB;vertical-align:top;";
    private const string Table = "border-collapse:collapse;width:100%;margin:4px 0 18px 0;";

    internal static string Subject(DateTime meetingDate) => $"Workload Meeting - {meetingDate:MMMM d, yyyy} - jobs and who is on them";

    internal static string Html(DateTime meetingDate, DateTime today, int weeks, IReadOnlyList<Job> jobs,
                                string? overallNotes, IReadOnlyList<string>? notAddressed = null)
    {
        var prioritised = jobs.Where(j => j.Priority >= 1 && j.Priority <= 5).ToList();
        var end = today.Date.AddDays(weeks * 7);
        var due = prioritised.Where(j => j.DueDate is { } d && d.Date >= today.Date && d.Date <= end)
                             .OrderBy(j => j.DueDate).ThenBy(j => j.Priority).ThenBy(j => j.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var noDate = prioritised.Where(j => j.DueDate is null)
                                .OrderBy(j => j.Priority).ThenBy(j => j.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var sb = new StringBuilder();
        sb.Append($"<div style=\"{Font}\">");
        sb.Append($"<p style=\"font-size:14pt;font-weight:600;margin:0 0 2px 0;\">Workload Meeting &ndash; {E(meetingDate.ToString("dddd, MMMM d, yyyy"))}</p>");
        sb.Append($"<p style=\"margin:0 0 16px 0;color:#4B5563;\">{prioritised.Count} prioritised jobs &middot; {due.Count} due in the next {weeks} weeks &middot; {noDate.Count} with no deadline in Deltek</p>");

        if (notAddressed is { Count: > 0 })
            sb.Append($"<p style=\"margin:0 0 14px 0;padding:6px 10px;background:#FEF3C7;color:#92400E;\">Not addressed (no email in Deltek): {E(string.Join(", ", notAddressed))}</p>");

        Heading(sb, $"Due in the next {weeks} weeks");
        if (due.Count == 0)
            sb.Append("<p style=\"margin:0 0 18px 0;color:#6B7280;\">None with a Deltek deadline in this window.</p>");
        else
        {
            Open(sb, "Due", "", "Project", "PM", "Drafting", "Deliverable", "Notes");
            foreach (var j in due)
                Row(sb, Bold(j.DueDate!.Value.ToString("ddd d MMM")), Chip(j.Priority), Project(j), E(j.Pm), E(j.DraftingManager), E(j.DueWhat), E(j.Notes));
            sb.Append("</table>");
        }

        if (noDate.Count > 0)
        {
            Heading(sb, $"No deadline in Deltek ({noDate.Count})", "#B45309");
            sb.Append("<p style=\"margin:0 0 6px 0;color:#6B7280;\">PMs: please enter the next deadline in the project's Deadlines grid in Deltek.</p>");
            Open(sb, "", "Project", "PM", "Drafting", "Notes");
            foreach (var j in noDate)
                Row(sb, Chip(j.Priority), Project(j), E(j.Pm), E(j.DraftingManager), E(j.Notes));
            sb.Append("</table>");
        }

        Heading(sb, "By project manager");
        foreach (var g in prioritised.GroupBy(j => Blank(j.Pm, "(No PM)"), StringComparer.OrdinalIgnoreCase)
                                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append($"<p style=\"margin:10px 0 2px 0;font-weight:600;\">{E(g.Key)} <span style=\"color:#6B7280;font-weight:400;\">({g.Count()})</span></p>");
            Open(sb, "", "Project", "Drafting", "Next due", "Notes");
            foreach (var j in g.OrderBy(j => j.Priority).ThenBy(j => j.Name, StringComparer.OrdinalIgnoreCase))
                Row(sb, Chip(j.Priority), Project(j), E(Blank(j.DraftingManager, "-")),
                    j.DueDate is { } d ? E(d.ToString("ddd d MMM")) : "<span style=\"color:#B45309;\">none</span>", E(j.Notes));
            sb.Append("</table>");
        }

        if (!string.IsNullOrWhiteSpace(overallNotes))
        {
            Heading(sb, "Meeting notes");
            sb.Append($"<p style=\"margin:0;white-space:pre-wrap;\">{E(overallNotes.Trim()).Replace("\r\n", "\n").Replace("\n", "<br>")}</p>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// Name ("First Last") -> mailbox from the staff directory (KorOpportunitiesDb opportunities.BdStaff,
    /// synced from Deltek EMMain every morning by BdStaffDirectorySyncJob). A local table, so the click
    /// does not wait on Deltek (Ian 27 Nov: "we know those emails - no need to look up every time").
    /// An active row wins over an inactive one for the same name.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> EmailsByName(IEnumerable<Kor.Opportunities.Data.Crm.BdStaffRow> staff)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in staff.OrderByDescending(s => s.IsActive))
        {
            var name = (s.DisplayName ?? "").Trim();
            var mail = (s.Mailbox ?? "").Trim();
            if (name.Length > 0 && mail.Contains('@')) map.TryAdd(name, mail);
        }
        return map;
    }

    /// <summary>The PMs and drafting managers of the prioritised jobs, as email addresses, in a
    /// stable order. Names with no address in Deltek are returned apart so the report can say so.</summary>
    internal static (IReadOnlyList<string> Emails, IReadOnlyList<string> Unmatched) Recipients(
        IEnumerable<Job> jobs, IReadOnlyDictionary<string, string> emailsByName)
    {
        var names = jobs.Where(j => j.Priority >= 1 && j.Priority <= 5)
                        .SelectMany(j => new[] { j.Pm, j.DraftingManager })
                        .Select(n => (n ?? "").Trim())
                        .Where(n => n.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        var emails = new List<string>();
        var unmatched = new List<string>();
        foreach (var n in names)
        {
            if (emailsByName.TryGetValue(n, out var e) && !string.IsNullOrWhiteSpace(e))
            {
                if (!emails.Contains(e, StringComparer.OrdinalIgnoreCase)) emails.Add(e);
            }
            else unmatched.Add(n);
        }
        return (emails, unmatched);
    }

    private static void Heading(StringBuilder sb, string text, string color = "#111827")
        => sb.Append($"<p style=\"font-size:12pt;font-weight:600;color:{color};margin:14px 0 4px 0;\">{E(text)}</p>");

    private static void Open(StringBuilder sb, params string[] headers)
    {
        sb.Append($"<table cellspacing=\"0\" cellpadding=\"0\" style=\"{Table}\"><tr>");
        foreach (var h in headers) sb.Append($"<th style=\"{Th}\">{E(h)}</th>");
        sb.Append("</tr>");
    }

    private static void Row(StringBuilder sb, params string[] cellsHtml)
    {
        sb.Append("<tr>");
        foreach (var c in cellsHtml) sb.Append($"<td style=\"{Td}\">{c}</td>");
        sb.Append("</tr>");
    }

    private static string Project(Job j) => $"<b>{E(j.Wbs1)}</b><br><span style=\"color:#4B5563;\">{E(j.Name)}</span>";

    private static string Bold(string s) => $"<b>{E(s)}</b>";

    private static string Chip(int p)
    {
        var bg = p switch { 1 => "#DC2626", 2 => "#EA580C", 3 => "#D97706", 4 => "#2563EB", _ => "#6B7280" };
        return $"<span style=\"background:{bg};color:#FFFFFF;font-weight:700;padding:2px 7px;\">P{p}</span>";
    }

    private static string E(string? s) => WebUtility.HtmlEncode((s ?? "").Trim());

    private static string Blank(string? s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
}
