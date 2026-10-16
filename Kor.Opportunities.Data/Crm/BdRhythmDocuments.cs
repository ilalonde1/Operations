#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Kor.Opportunities.Data.Crm;

/// <summary>A generated agenda: a title, timed sections, and the plain text to paste into an invite.</summary>
public sealed record BdAgendaDoc(string Title, int Minutes, IReadOnlyList<BdAgendaSection> Sections)
{
    public string ToText()
    {
        var sb = new StringBuilder().Append(Title).Append(" (").Append(Minutes.ToString(CultureInfo.InvariantCulture)).AppendLine(" minutes)").AppendLine();
        // A section with nothing to discuss is left out of the invite rather than printed as "Nothing."
        foreach (var s in Sections.Where(s => s.Lines.Count > 0 || s.IsPrompt))
        {
            sb.Append(s.Heading);
            if (s.Minutes > 0) sb.Append(" (").Append(s.Minutes.ToString(CultureInfo.InvariantCulture)).Append(" min)");
            sb.AppendLine();
            foreach (var l in s.Lines) sb.Append("  - ").AppendLine(l);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>An agenda section. A prompt section is a discussion item with no data lines, so it never reads "Nothing".</summary>
public sealed record BdAgendaSection(string Heading, int Minutes, IReadOnlyList<string> Lines, bool IsPrompt = false);

/// <summary>
/// Agendas for the rhythm Ian committed to on 9 October: a 20-minute one-to-one with each regional lead and a 27-minute
/// all-hands. Built only from the action register, so the agenda is the open work, not a blank page.
/// </summary>
public static class BdAgenda
{
    public static BdAgendaDoc ForOneToOne(string leadName, IEnumerable<BdActionRow> leadActions, DateTimeOffset nowUtc, DateTimeOffset? lastMetUtc)
    {
        var actions = leadActions.ToList();
        var open = actions.Where(a => a.IsOpen).OrderBy(a => a.DueUtc).ToList();
        var overdue = open.Where(a => a.DueUtc < nowUtc).Select(Line).ToList();
        var soon = open.Where(a => a.DueUtc >= nowUtc && a.DueUtc < nowUtc.AddDays(14)).Select(Line).ToList();
        var since = lastMetUtc ?? nowUtc.AddDays(-14);
        var later = open.Count(a => a.DueUtc >= nowUtc.AddDays(14));
        if (later > 0) soon.Add($"Also open, due later: {later.ToString(CultureInfo.InvariantCulture)}");
        var done = actions.Where(a => !a.IsOpen && a.ClosedAtUtc >= since).Select(a => $"{Clean(a.Action)} ({a.Status}{(string.IsNullOrWhiteSpace(a.Result) ? "" : ": " + a.Result)})").ToList();
        return new BdAgendaDoc($"One-to-one: {leadName}", 20, new[]
        {
            new BdAgendaSection("Overdue", 6, overdue),
            new BdAgendaSection("Due in the next two weeks", 6, soon),
            new BdAgendaSection("Done since we last met", 3, done),
            new BdAgendaSection("What is new in your region, and the one thing in your way", 5, Array.Empty<string>(), IsPrompt: true),
        });
    }

    public static BdAgendaDoc ForAllHands(IEnumerable<BdActionRow> allActions, DateTimeOffset nowUtc, DateTimeOffset? lastMetUtc)
    {
        var actions = allActions.ToList();
        var overdue = actions.Where(a => a.IsOpen && a.DueUtc < nowUtc)
            .GroupBy(a => a.OwnerName).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}: {g.Count().ToString(CultureInfo.InvariantCulture)} overdue: {string.Join("; ", g.OrderBy(a => a.DueUtc).Take(3).Select(a => Clean(a.Action)))}")
            .ToList();
        var since = lastMetUtc ?? nowUtc.AddDays(-28);
        var done = actions.Where(a => !a.IsOpen && a.Status == "Done" && a.ClosedAtUtc >= since)
            .OrderBy(a => a.OwnerName, StringComparer.Ordinal).Select(a => $"{a.OwnerName}: {Clean(a.Action)}").ToList();
        return new BdAgendaDoc("BD all-hands", 27, new[]
        {
            new BdAgendaSection("Done since the last meeting", 4, done),
            new BdAgendaSection("Late, by owner", 10, overdue),
            new BdAgendaSection("One topic", 10, Array.Empty<string>(), IsPrompt: true),
            new BdAgendaSection("Next month's focus, and the next date", 3, Array.Empty<string>(), IsPrompt: true),
        });
    }

    private static string Line(BdActionRow a) => $"{Clean(a.Action)} (due {a.DueUtc.ToString("d MMM", CultureInfo.InvariantCulture)})";

    /// <summary>An action without its closing full stop, so it joins and brackets cleanly.</summary>
    private static string Clean(string action) => action.Trim().TrimEnd('.');
}

/// <summary>
/// The Monday list: each regional lead's open actions, overdue first. Rendered as one email of drafts addressed to Ian; nothing
/// is sent to the leads until Ian decides to (his instruction, 15 October 2026).
/// </summary>
public static class BdLeadLists
{
    public static IReadOnlyList<(string OwnerKey, string OwnerName, IReadOnlyList<BdActionRow> Actions)> Group(IEnumerable<BdActionRow> actions) =>
        actions.Where(a => a.IsOpen)
            .GroupBy(a => (a.OwnerKey, a.OwnerName))
            .OrderBy(g => g.Key.OwnerName, StringComparer.Ordinal)
            .Select(g => (g.Key.OwnerKey, g.Key.OwnerName, (IReadOnlyList<BdActionRow>)g.OrderBy(a => a.DueUtc).ToList()))
            .ToList();

    public static string RenderDraftsEmail(IEnumerable<BdActionRow> actions, DateTimeOffset nowUtc)
    {
        var sb = new StringBuilder("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:15px;line-height:1.5;color:#17212c;max-width:760px\">");
        sb.Append("<h1 style=\"font-size:22px;margin:0 0 4px\">Monday lists, drafts for you</h1>")
          .Append("<p style=\"color:#5a6876;margin:0 0 16px\">One section per person, from the action register. Nothing has been sent to anyone; forward a section when you are ready.</p>");
        foreach (var (_, name, list) in Group(actions))
        {
            sb.Append("<h2 style=\"font-size:17px;margin:20px 0 6px\">").Append(E(name)).Append("</h2><ul style=\"padding-left:20px;margin:0\">");
            foreach (var a in list)
            {
                var late = a.DueUtc < nowUtc;
                sb.Append("<li style=\"margin:4px 0\"><b").Append(late ? " style=\"color:#b3261e\"" : string.Empty).Append('>')
                  .Append(late ? "Overdue since " : "Due ").Append(a.DueUtc.ToString("ddd d MMM", CultureInfo.InvariantCulture)).Append("</b>: ")
                  .Append(E(a.Action));
                if (!string.IsNullOrWhiteSpace(a.WhyNow)) sb.Append(" <span style=\"color:#5a6876\">").Append(E(a.WhyNow)).Append("</span>");
                sb.Append("</li>");
            }

            sb.Append("</ul>");
        }

        return sb.Append("</div>").ToString();
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
