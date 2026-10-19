#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kor.Opportunities.Data.BdReports.Generators;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// The prepared hour: what a BD director would hand Ian each morning. Written in Claude's session from the live store, Deltek,
/// the meetings and the books (CLAUDE.md rule 14: no model call at runtime), imported by `BdKnowledgeIngest hour import`,
/// shown on the Director's Today tab and emailed by the Worker at 6 a.m. to Ian only.
/// </summary>
public sealed record DirectorHourContent(
    string ForDate,
    string Kind,
    string Title,
    string? Lede,
    IReadOnlyList<HourPlanLine> Plan,
    string? Callout,
    IReadOnlyList<HourMove> Moves,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<string> Meetings,
    string? Sources,
    string? NotCovered)
{
    public IReadOnlyList<HourSection>? Sections { get; init; }
}

public sealed record HourSection(string Role, string Title, IReadOnlyList<HourPlanLine> Plan,
    IReadOnlyList<HourMove> Moves, IReadOnlyList<string> Decisions);

/// <summary>One line of "where we stand". Status is off, slow, blocked or on.</summary>
public sealed record HourPlanLine(string Status, string Label, string Text);

public sealed record HourMove(
    int Rank,
    string Title,
    int Minutes,
    IReadOnlyList<string> WhyNow,
    HourPrinciple? Principle,
    IReadOnlyList<HourDraft> Drafts,
    IReadOnlyList<string> Steps,
    IReadOnlyList<HourLink> Links)
{
    public IReadOnlyList<long> Cards { get; init; } = Array.Empty<long>();
}

/// <summary>A passage from the books, quoted with its source.</summary>
public sealed record HourPrinciple(string Quote, string Cite);

/// <summary>A message drafted for Ian to copy and send himself. Nothing is sent by the platform.</summary>
public sealed record HourDraft(string To, string Subject, string Body);

/// <summary>A button into an existing KOR Operations screen. Kind: org, opp, pursuit, mpi, person or director.</summary>
public sealed record HourLink(string Label, string Kind, long Id)
{
    public string? Url => Kind switch
    {
        "org" => KorReportLinks.Org(Id),
        "opp" => KorReportLinks.Opportunity(Id),
        "pursuit" => KorReportLinks.Pursuit(Id),
        "mpi" => KorReportLinks.Mpi(Id),
        "person" => KorReportLinks.Person(Id),
        "director" => KorReportLinks.Director(Id),
        _ => null,
    };
}

public static class DirectorHourFile
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] PlanStatuses = { "off", "slow", "blocked", "on" };
    private static readonly string[] LinkKinds = { "org", "opp", "pursuit", "mpi", "person", "director" };

    /// <summary>Parses and validates a prepared hour. Every problem is reported; nothing is accepted with errors.</summary>
    public static (DirectorHourContent? Content, IReadOnlyList<string> Errors) Parse(string json)
    {
        var errors = new List<string>();
        DirectorHourContent? c;
        try
        {
            c = JsonSerializer.Deserialize<DirectorHourContent>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return (null, new[] { "Not valid JSON: " + ex.Message });
        }

        if (c is null) return (null, new[] { "The file is empty." });
        if (!DateOnly.TryParseExact(c.ForDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors.Add("forDate must be yyyy-MM-dd.");
        if (!BdRhythmValues.HourKinds.Contains(c.Kind)) errors.Add("kind must be BD, Operations or Morning.");
        if (string.IsNullOrWhiteSpace(c.Title)) errors.Add("title is required.");
        void ValidatePlan(IReadOnlyList<HourPlanLine>? plan, string at)
        {
            if (plan is null) { errors.Add($"{at}: plan is required (an empty list is allowed)."); return; }
            foreach (var line in plan)
            {
                if (line is null) { errors.Add($"{at}: plan line cannot be null."); continue; }
                if (!PlanStatuses.Contains(line.Status)) errors.Add($"{at}: plan status '{line.Status}' must be off, slow, blocked or on.");
                if (string.IsNullOrWhiteSpace(line.Label) || string.IsNullOrWhiteSpace(line.Text)) errors.Add($"{at}: every plan line needs a label and text.");
            }
        }

        if (c.Kind == "Morning")
        {
            if (c.Moves is { Count: > 0 }) errors.Add("Morning must have no top-level moves; put moves in sections.");
            if (c.Sections is null || c.Sections.Count is < 2 or > 3) errors.Add("Morning sections must hold 2 or 3 sections.");
            var roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in c.Sections ?? Array.Empty<HourSection>())
            {
                if (section is null) { errors.Add("Morning section cannot be null."); continue; }
                var at = $"section '{section.Role}'";
                if (section.Role is not ("BD" or "COO" or "IT")) errors.Add($"{at}: unknown role; use BD, COO or IT.");
                else if (!roles.Add(section.Role)) errors.Add($"{at}: duplicate role.");
                if (string.IsNullOrWhiteSpace(section.Title)) errors.Add($"{at}: title is required.");
                ValidatePlan(section.Plan, at);
                if (section.Moves is null || section.Moves.Count is < 1 or > 4) errors.Add($"{at}: moves must hold 1 to 4 moves.");
            }
            if (c.Plan is not null) ValidatePlan(c.Plan, "hour");
        }
        else
        {
            ValidatePlan(c.Plan, "hour");
            if (c.Sections is { Count: > 0 }) errors.Add($"{c.Kind} hours cannot have sections; use kind Morning.");
            if (c.Moves is null || c.Moves.Count is < 1 or > 7) errors.Add("moves must hold 1 to 7 moves.");
        }

        var moves = AllMoves(c).ToArray();
        if (moves.Any(m => m is null)) errors.Add("move cannot be null.");
        else
        {
            var ranks = moves.Select(m => m.Rank).OrderBy(r => r).ToArray();
            if (!ranks.SequenceEqual(Enumerable.Range(1, ranks.Length))) errors.Add("move ranks must run 1, 2, 3 ... without gaps and be unique across the whole hour.");
            foreach (var m in moves)
            {
                var at = $"move {m.Rank}";
                if (m.Cards is null || m.Cards.Any(id => id <= 0) || m.Cards.Distinct().Count() != m.Cards.Count)
                    errors.Add($"{at}: cards must contain positive, unique card ids.");
                if (string.IsNullOrWhiteSpace(m.Title)) errors.Add($"{at}: title is required.");
                if (m.Minutes is < 5 or > 90) errors.Add($"{at}: minutes must be 5 to 90.");
                if (m.WhyNow is null || m.WhyNow.Count == 0 || m.WhyNow.Any(string.IsNullOrWhiteSpace)) errors.Add($"{at}: whyNow needs at least one line.");
                if (m.Principle is { } p && (string.IsNullOrWhiteSpace(p.Quote) || string.IsNullOrWhiteSpace(p.Cite))) errors.Add($"{at}: a principle needs a quote and a cite.");
                foreach (var d in m.Drafts ?? Array.Empty<HourDraft>())
                    if (string.IsNullOrWhiteSpace(d.To) || string.IsNullOrWhiteSpace(d.Subject) || string.IsNullOrWhiteSpace(d.Body)) errors.Add($"{at}: every draft needs to, subject and body.");
                foreach (var l in m.Links ?? Array.Empty<HourLink>())
                {
                    if (string.IsNullOrWhiteSpace(l.Label)) errors.Add($"{at}: every link needs a label.");
                    if (!LinkKinds.Contains(l.Kind)) errors.Add($"{at}: link kind '{l.Kind}' must be org, opp, pursuit, mpi, person or director.");
                    else if (l.Id <= 0 || (l.Kind == "director" && l.Id > KorReportLinks.DirectorReferences)) errors.Add($"{at}: link '{l.Label}' has no valid id.");
                }
            }
        }

        return errors.Count == 0 ? (Normalise(c), errors) : (null, errors);
    }

    private static DirectorHourContent Normalise(DirectorHourContent c) => c with
    {
        Plan = c.Plan ?? Array.Empty<HourPlanLine>(),
        Moves = NormaliseMoves(c.Moves ?? Array.Empty<HourMove>()),
        Sections = c.Sections?.Select(s => s with
        {
            Moves = NormaliseMoves(s.Moves), Decisions = s.Decisions ?? Array.Empty<string>(),
        }).ToArray(),
        Decisions = c.Decisions ?? Array.Empty<string>(),
        Meetings = c.Meetings ?? Array.Empty<string>(),
    };

    private static IReadOnlyList<HourMove> NormaliseMoves(IEnumerable<HourMove> moves) => moves.OrderBy(m => m.Rank).Select(m => m with
    {
        Drafts = m.Drafts ?? Array.Empty<HourDraft>(), Steps = m.Steps ?? Array.Empty<string>(), Links = m.Links ?? Array.Empty<HourLink>(),
    }).ToArray();

    public static IEnumerable<HourMove> AllMoves(DirectorHourContent c) => c.Kind == "Morning"
        ? (c.Sections ?? Array.Empty<HourSection>()).Where(s => s is not null).SelectMany(s => s.Moves ?? Array.Empty<HourMove>())
        : c.Moves ?? Array.Empty<HourMove>();

    public static int TotalMinutes(DirectorHourContent c) => AllMoves(c).Sum(m => m.Minutes);

    /// <summary>Import gate: a citation must be an accepted card for this hour's role.</summary>
    public static IReadOnlyList<string> ValidateCards(DirectorHourContent content, IReadOnlyList<DoctrineCardRow> cards)
    {
        var sections = content.Kind == "Morning" ? content.Sections ?? Array.Empty<HourSection>()
            : new[] { new HourSection(content.Kind, content.Title, content.Plan, content.Moves, content.Decisions) };
        return sections.SelectMany(s =>
        {
            var role = s.Role == "COO" ? "Operations" : s.Role;
            var accepted = cards.Where(c => c.Role == role && c.Status == "accepted").Select(c => c.Id).ToHashSet();
            return s.Moves.SelectMany(m => m.Cards.Where(id => !accepted.Contains(id))
                .Select(id => (content.Kind == "Morning" ? $"section {s.Role}, " : "")
                    + $"move {m.Rank}: card {id} is not an accepted {role} card."));
        }).ToArray();
    }
}

/// <summary>
/// Email-safe HTML for the 6 a.m. hour (inline styles, tables, no scripts). Every link is a kor:// address that opens the
/// screen in KOR Operations on a machine where it is installed.
/// </summary>
public static class DirectorHourEmail
{
    public static string Subject(DirectorHourContent c) =>
        $"{(c.Kind == "Morning" ? "Your morning" : c.Title)} · about {DirectorHourFile.TotalMinutes(c)} minutes";

    /// <param name="hourId">The stored hour's id. When given, every move carries Done / Not today / Not doing links that
    /// record the answer in KOR Operations (kor://move), so the hour can be marked from the email itself.</param>
    public static string Render(DirectorHourContent c, long? hourId = null)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:15px;line-height:1.5;color:#17212c;max-width:760px\">");
        sb.Append("<h1 style=\"font-size:24px;margin:0 0 4px\">").Append(E(c.Title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(c.Lede)) sb.Append("<p style=\"color:#5a6876;margin:0 0 16px\">").Append(E(c.Lede)).Append("</p>");
        sb.Append("<p style=\"margin:0 0 16px\"><a href=\"").Append(KorReportLinks.Director(KorReportLinks.DirectorToday))
          .Append("\" style=\"color:#2e4a62;font-weight:600\">Open today in KOR Operations</a></p>");

        RenderPlan(c.Plan, sb);

        if (!string.IsNullOrWhiteSpace(c.Callout))
            sb.Append("<p style=\"background:#fdf2dc;border-left:4px solid #8a5a00;padding:10px 12px;margin:14px 0\">").Append(E(c.Callout)).Append("</p>");

        if (c.Kind == "Morning")
        {
            foreach (var section in c.Sections ?? Array.Empty<HourSection>())
            {
                sb.Append("<h2 style=\"font-size:21px;margin:24px 0 8px\">").Append(E(section.Role)).Append("</h2>");
                sb.Append("<p><b>").Append(E(section.Title)).Append("</b></p>");
                RenderPlan(section.Plan, sb);
                RenderMoves(section.Moves, sb, hourId);
                List("Decisions waiting on you", section.Decisions, sb);
            }
        }
        else RenderMoves(c.Moves, sb, hourId);

        List("Decisions waiting on you", c.Decisions, sb);
        List("Meetings", c.Meetings, sb);
        if (!string.IsNullOrWhiteSpace(c.Sources))
            sb.Append("<p style=\"color:#5a6876;font-size:13px;margin-top:20px\"><b>How this was prepared.</b> ").Append(E(c.Sources)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(c.NotCovered))
            sb.Append("<p style=\"color:#5a6876;font-size:13px\"><b>Not covered yet.</b> ").Append(E(c.NotCovered)).Append("</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static void RenderPlan(IReadOnlyList<HourPlanLine> plan, StringBuilder sb)
    {
        if (plan.Count > 0)
        {
            sb.Append("<h2 style=\"font-size:17px;margin:20px 0 8px\">Where we stand</h2><table cellpadding=\"6\" style=\"border-collapse:collapse;width:100%\">");
            foreach (var p in plan)
                sb.Append("<tr><td style=\"white-space:nowrap;vertical-align:top;font-weight:600;color:").Append(StatusColour(p.Status)).Append("\">")
                  .Append(E(p.Label)).Append("</td><td style=\"border-bottom:1px solid #dbe2e9\">").Append(E(p.Text)).Append("</td></tr>");
            sb.Append("</table>");
        }

    }

    private static void RenderMoves(IReadOnlyList<HourMove> moves, StringBuilder sb, long? hourId)
    {
        sb.Append("<h2 style=\"font-size:17px;margin:22px 0 8px\">Today's moves</h2>");
        foreach (var m in moves)
        {
            sb.Append("<div style=\"border:1px solid #dbe2e9;border-radius:6px;padding:12px 14px;margin:0 0 12px\">");
            sb.Append("<div style=\"font-size:16px;font-weight:700\">").Append(m.Rank.ToString(CultureInfo.InvariantCulture)).Append(". ")
              .Append(E(m.Title)).Append(" <span style=\"font-weight:400;color:#5a6876\">· ").Append(m.Minutes.ToString(CultureInfo.InvariantCulture)).Append(" min</span></div>");
            sb.Append("<ul style=\"margin:8px 0;padding-left:20px\">");
            foreach (var w in m.WhyNow) sb.Append("<li>").Append(E(w)).Append("</li>");
            sb.Append("</ul>");
            if (m.Principle is { } p)
                sb.Append("<blockquote style=\"margin:8px 0;padding:8px 12px;border-left:3px solid #2e4a62;background:#e6edf3\">")
                  .Append(E(p.Quote)).Append("<br><span style=\"color:#5a6876;font-size:13px\">").Append(E(p.Cite)).Append("</span></blockquote>");
            foreach (var d in m.Drafts)
                sb.Append("<div style=\"border:1px solid #dbe2e9;border-radius:4px;margin:8px 0\"><div style=\"background:#f7f9fb;padding:6px 10px;font-size:13px\">To ")
                  .Append(E(d.To)).Append(" · <b>").Append(E(d.Subject)).Append("</b></div><div style=\"padding:8px 10px;white-space:pre-wrap\">")
                  .Append(E(d.Body)).Append("</div></div>");
            if (m.Steps.Count > 0)
            {
                sb.Append("<ol style=\"margin:8px 0;padding-left:20px\">");
                foreach (var s in m.Steps) sb.Append("<li>").Append(E(s)).Append("</li>");
                sb.Append("</ol>");
            }

            if (m.Links.Count > 0)
            {
                sb.Append("<p style=\"margin:6px 0 0\">");
                foreach (var l in m.Links)
                    if (l.Url is { } url)
                        sb.Append("<a href=\"").Append(E(url)).Append("\" style=\"color:#2e4a62;margin-right:14px\">").Append(E(l.Label)).Append("</a>");
                sb.Append("</p>");
            }

            // The answer feeds each cited card's record, which weights its ranking tomorrow.
            if (hourId is { } id)
            {
                sb.Append("<p style=\"margin:10px 0 0;padding-top:8px;border-top:1px solid #dbe2e9;font-size:13px;color:#5a6876\">Mark it: ");
                foreach (var (result, label, colour) in new[] { ("Done", "Done", "#1f6b3a"), ("NotToday", "Not today", "#8a5a00"), ("NotDoing", "Not doing", "#b3261e") })
                    sb.Append("<a href=\"").Append(E(KorReportLinks.Move(id, m.Rank, result))).Append("\" style=\"color:").Append(colour)
                      .Append(";font-weight:600;margin-right:16px\">").Append(label).Append("</a>");
                sb.Append("</p>");
            }

            sb.Append("</div>");
        }

    }

    private static void List(string heading, IReadOnlyList<string> items, StringBuilder sb)
    {
        if (items.Count == 0) return;
        sb.Append("<h2 style=\"font-size:17px;margin:20px 0 8px\">").Append(E(heading)).Append("</h2><ul style=\"padding-left:20px\">");
        foreach (var i in items) sb.Append("<li>").Append(E(i)).Append("</li>");
        sb.Append("</ul>");
    }

    private static string StatusColour(string status) => status switch
    {
        "off" => "#b3261e",
        "slow" => "#8a5a00",
        "blocked" => "#2e4a62",
        _ => "#1f6b3a",
    };

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
