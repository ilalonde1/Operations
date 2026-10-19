using System.Globalization;
using Kor.Opportunities.Data.Crm;

namespace Kor.MorningBrief;

/// <summary>
/// The meetings the virtual BD Manager and COO run for Ian (migration 369): which are due to prepare, booked, passed without a
/// record, or not due yet, each with its people's open actions so the hour can write the agenda, pre-read and asks. The hour
/// writes each prepared meeting as a move whose drafts are the invite (with when, minutes and cadenceId) and the pre-read.
/// Pure formatting; no store reads.
/// </summary>
public static class MorningMeetings
{
    public static MorningFact Facts(string name, IReadOnlyList<MeetingDue> due, IReadOnlyList<BdActionRow> actions,
        DateTimeOffset now, TimeZoneInfo zone)
    {
        var lines = new List<string>();
        var situations = new List<MorningSituation>();
        foreach (var d in due.OrderBy(StateOrder).ThenBy(d => d.ProposedUtc))
        {
            var c = d.Cadence;
            var people = c.Attendees.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var open = actions.Where(a => a.IsOpen && people.Contains(a.OwnerKey)).OrderBy(a => a.DueUtc).ToList();
            var last = d.LastHeldUtc is { } held ? Local(held, zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "never";
            var head = $"cadence {c.Id}: {c.Title} ({c.Seat}, {c.DurationMinutes} min, every {c.EveryDays} days); with {string.Join("; ", c.Attendees)}; last held {last}";
            switch (d.State)
            {
                case "Prepare":
                    var at = Local(d.ProposedUtc, zone);
                    lines.Add(FormattableString.Invariant($"PREPARE {head}; propose {at:ddd d MMM HH:mm} (when {at:yyyy-MM-ddTHH:mm:sszzz}); ")
                        + $"{open.Count} open actions for these people{(open.Count == 0 ? "." : ": " + string.Join("; ", open.Take(8).Select(a => ActionText(a, now))))}");
                    situations.Add(new MorningSituation("meeting due",
                        $"A recurring {c.Cadence.ToLowerInvariant()} meeting is due: prepare its invite, a timed agenda, a pre-read and one ask per person.",
                        lines[^1], new[] { "meeting-due" }));
                    break;
                case "NotRecorded":
                    lines.Add(FormattableString.Invariant($"NOT RECORDED {head}; planned for {Local(d.PlannedUtc!.Value, zone):ddd d MMM HH:mm} and passed without being marked held or cancelled. Ask Ian: held or not, and the minutes."));
                    situations.Add(new MorningSituation("meeting not recorded",
                        "A planned meeting passed with no record of what was decided; get the minutes and the asks onto the register.",
                        lines[^1], new[] { "meeting-unrecorded" }));
                    break;
                case "Planned":
                    lines.Add(FormattableString.Invariant($"PLANNED {head}; booked for {Local(d.PlannedUtc!.Value, zone):ddd d MMM HH:mm}."));
                    break;
                default:
                    var next = Local(d.ProposedUtc, zone);
                    lines.Add(FormattableString.Invariant($"LATER {head}; next {next:ddd d MMM HH:mm}, prepared from {next.AddDays(-MeetingsDue.LeadDays(c)):ddd d MMM}."));
                    break;
            }
        }
        var owed = due.Count(d => d.State is "Prepare" or "NotRecorded");
        return new MorningFact($"{name} ({owed} of {due.Count} need you)", lines, lines.Count, due.Count) { Situations = situations };
    }

    private static int StateOrder(MeetingDue d) => d.State switch { "Prepare" => 0, "NotRecorded" => 1, "Planned" => 2, _ => 3 };

    private static DateTimeOffset Local(DateTimeOffset utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(utc, zone);

    private static string ActionText(BdActionRow a, DateTimeOffset now) =>
        FormattableString.Invariant($"{a.OwnerName}: #{a.Id} {a.Action.Trim().TrimEnd('.')} (due {a.DueUtc:yyyy-MM-dd}{(a.DueUtc < now ? ", overdue" : "")})");
}
