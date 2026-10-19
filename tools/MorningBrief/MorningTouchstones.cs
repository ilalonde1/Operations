using System.Globalization;
using Kor.Opportunities.Data.Crm;

namespace Kor.MorningBrief;

/// <summary>
/// Pursuit touchstones (migration 371): for each pursuit owner, what is due on their pursuits and whether their weekly roll-up
/// is owed today. The hour writes each owed roll-up as one draft addressed to the owner, opened unsent in Ian's Outlook, with
/// the printed keys in draft.touchstones. Pure formatting; no store reads.
/// </summary>
public static class MorningTouchstones
{
    public const int ShownPerOwner = 8;

    public static MorningFact Facts(IReadOnlyList<OwnerRollup> rollups, DateTimeOffset now, TimeZoneInfo zone)
    {
        var lines = new List<string>();
        var situations = new List<MorningSituation>();
        foreach (var r in rollups)
        {
            var last = r.LastDraftedUtc is { } at ? Local(at, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "never";
            if (!r.IsOwed)
            {
                lines.Add(r.Due.Count == 0
                    ? $"NOTHING DUE {r.OwnerName} <{r.OwnerKey}>: {r.Pursuits} live pursuits; last roll-up {last}."
                    : FormattableString.Invariant($"NOT TODAY {r.OwnerName} <{r.OwnerKey}>: {r.Due.Count} of {r.Pursuits} live pursuits have a touchstone due, none urgent; last roll-up {last}; next roll-up {r.NextOn:yyyy-MM-dd}."));
                continue;
            }
            var shown = r.Due.Take(ShownPerOwner).ToList();
            lines.Add($"ROLL-UP {r.OwnerName} <{r.OwnerKey}>: {r.Due.Count} of {r.Pursuits} live pursuits have a touchstone due; "
                + $"{r.Due.Count(d => d.IsUrgent)} urgent; last roll-up {last}. Showing {shown.Count} of {r.Due.Count}.");
            foreach (var d in shown)
            {
                lines.Add(Item(r, d, now, zone));
                if (situations.All(s => s.Kind != "pursuit touchstone: " + d.Touchstone.Name))
                    situations.Add(new MorningSituation("pursuit touchstone: " + d.Touchstone.Name,
                        $"A pursuit has reached a touchstone, {d.Touchstone.Name.ToLowerInvariant()}: {d.Touchstone.Carries}",
                        lines[^1], d.Touchstone.Situations.ToArray()));
            }
        }
        var owed = rollups.Count(r => r.IsOwed);
        return new MorningFact($"Pursuit touchstones ({owed} of {rollups.Count} owners owe a roll-up today)", lines, owed, rollups.Count)
        { Situations = situations };
    }

    private static string Item(OwnerRollup r, TouchstoneDue d, DateTimeOffset now, TimeZoneInfo zone)
    {
        var p = d.Pursuit;
        var title = string.IsNullOrWhiteSpace(p.Title) ? (string.IsNullOrWhiteSpace(p.Buyer) ? "(untitled)" : p.Buyer) : p.Title;
        var history = p.KorProjects is > 0
            ? FormattableString.Invariant($"{p.KorProjects} Deltek project records with this buyer{(p.LastKorProject is { } k ? $", latest started {k:yyyy-MM}" : "")}")
            : p.BuyerOrgId is null ? "buyer not linked to an org" : "no Deltek projects with this buyer on file";
        var checklist = p.Requirements > 0 ? $"checklist {p.OpenRequirements} of {p.Requirements} open" : "no checklist loaded";
        var next = p.NextActionDueUtc is { } n ? FormattableString.Invariant($"; next action {p.NextActionNote ?? "(no note)"} due {Local(n, zone):yyyy-MM-dd}") : "";
        return FormattableString.Invariant($"  {r.OwnerName} · {d.Touchstone.Name}{(d.IsUrgent ? " (urgent)" : "")} · pursuit #{p.EngagementId} {title}")
            + $" · {(p.IsTender ? "tender" : "relationship")}, {Stage(p.Stage)} · buyer {(string.IsNullOrWhiteSpace(p.Buyer) ? "unknown" : p.Buyer)}"
            + FormattableString.Invariant($" · due since {d.DueFrom:yyyy-MM-dd} ({Anchor(d, now, zone)})")
            + $" · proposed fee {p.ProposedFee?.ToString("N0", CultureInfo.InvariantCulture) ?? "unknown"} · {history} · {checklist}"
            + $" · latest news: {p.LatestNews ?? "none in 180 days"}{next} · carries: {d.Touchstone.Carries} · key {d.Key}";
    }

    private static string Anchor(TouchstoneDue d, DateTimeOffset now, TimeZoneInfo zone)
    {
        var day = d.AnchorDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return d.Touchstone.Anchor switch
        {
            "Opened" => $"opened {day}",
            "Deadline" => $"deadline {day}",
            "Submitted" => $"submitted {day}",
            "LastTouch" => FormattableString.Invariant($"last touch or roll-up {day}, {(int)(now - d.Pursuit.LastTouchUtc).TotalDays} days since the last touch"),
            "NextAction" => $"next action due {day}",
            _ => $"closed {day}",
        };
    }

    private static string Stage(int stage) => stage switch { 1 => "pending", 3 => "submitted", 6 => "won", 7 => "lost", _ => "stage " + stage };

    private static DateTimeOffset Local(DateTimeOffset utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(utc, zone);
}
