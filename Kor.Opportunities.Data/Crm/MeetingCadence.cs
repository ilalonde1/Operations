#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// A recurring meeting of the virtual BD Manager or COO (migration 369). Attendees are staff keys; Ian is implied. Weekday 1 is
/// Monday; the time is Vancouver local. FirstOnOrAfter holds the first occurrence back (the BD monthly waits for 13 Nov 2026).
/// </summary>
public sealed record MeetingCadenceRow(int Id, string Seat, string Cadence, string MeetingKind, string Title, string? WithStaffKey,
    IReadOnlyList<string> Attendees, int DurationMinutes, int EveryDays, int PreferredWeekday, TimeOnly PreferredLocalTime,
    DateOnly? FirstOnOrAfter);

/// <summary>Where one recurring meeting stands today.</summary>
/// <param name="State">Prepare: due inside its lead time with nothing planned, so the invite and pre-read are owed now.
/// Planned: an occurrence is booked ahead. NotRecorded: a planned occurrence has passed without being marked held or
/// cancelled. Later: not due yet.</param>
public sealed record MeetingDue(MeetingCadenceRow Cadence, string State, DateTimeOffset? LastHeldUtc, DateTimeOffset? PlannedUtc,
    DateTimeOffset ProposedUtc);

/// <summary>Who an Outlook invite for a stored meeting goes to.</summary>
public static class MeetingInvite
{
    private static readonly Dictionary<string, string> Nicknames = new(StringComparer.OrdinalIgnoreCase) { ["Jim"] = "James", ["James"] = "Jim" };

    /// <summary>
    /// A one-to-one goes to its person. Otherwise each name in Attendees ("Ian, Jim, John" or "Ian Lalonde, Omar Alcazar Pastrana")
    /// becomes that lead's staff address, matched on the full name or the first name (Jim and James are the same person);
    /// Ian is left out, notes in brackets are dropped, and a name that matches no lead is kept for Outlook to resolve.
    /// </summary>
    public static IReadOnlyList<string> Recipients(BdMeetingRow meeting, IReadOnlyList<BdLeadRow> leads, string ianKey = "ilalonde@korstructural.com")
    {
        if (meeting.Kind == "OneToOne" && !string.IsNullOrWhiteSpace(meeting.WithStaffKey)) return new[] { meeting.WithStaffKey!.Trim() };
        var text = System.Text.RegularExpressions.Regex.Replace(meeting.Attendees ?? "", @"\([^)]*\)", "");
        var result = new List<string>();
        foreach (var name in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            var lead = leads.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? leads.FirstOrDefault(l => FirstName(l.Name) is { } f
                    && (string.Equals(f, first, StringComparison.OrdinalIgnoreCase)
                        || (Nicknames.TryGetValue(first, out var alt) && string.Equals(f, alt, StringComparison.OrdinalIgnoreCase))));
            var address = lead?.StaffKey ?? name;
            if (string.Equals(address, ianKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (!result.Contains(address, StringComparer.OrdinalIgnoreCase)) result.Add(address);
        }
        return result;
    }

    private static string? FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
}

public static class MeetingsDue
{
    public static readonly IReadOnlyList<string> States = new[] { "Prepare", "Planned", "NotRecorded", "Later" };

    /// <summary>The Vancouver zone the cadence times are written in; Windows and IANA ids both resolve.</summary>
    public static TimeZoneInfo Pacific()
    {
        foreach (var id in new[] { "Pacific Standard Time", "America/Vancouver" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new TimeZoneNotFoundException("Neither 'Pacific Standard Time' nor 'America/Vancouver' is known on this machine.");
    }

    /// <summary>Invites go out this far ahead: two weeks for a monthly, one for a quickie.</summary>
    public static int LeadDays(MeetingCadenceRow c) => c.Cadence == "Monthly" ? 14 : 7;

    /// <summary>
    /// Each active cadence's state. A meeting counts toward a cadence when it carries the cadence's id, or, for meetings planned
    /// before migration 369, when it is the same kind with the same person (a one-to-one) or any all-hands (the BD monthly).
    /// The next occurrence is the last held one plus the interval, never before FirstOnOrAfter or tomorrow, moved forward to
    /// the preferred weekday.
    /// </summary>
    public static IReadOnlyList<MeetingDue> Compute(IEnumerable<MeetingCadenceRow> cadences, IEnumerable<BdMeetingRow> meetings,
        DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var all = meetings.ToList();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);
        var result = new List<MeetingDue>();
        foreach (var c in cadences)
        {
            var mine = all.Where(m => Belongs(m, c)).ToList();
            var lastHeld = mine.Where(m => m.Status == "Held").Select(m => (DateTimeOffset?)m.ScheduledForUtc).Max();
            var planned = mine.Where(m => m.Status == "Planned").OrderBy(m => m.ScheduledForUtc).ToList();
            var ahead = planned.FirstOrDefault(m => m.ScheduledForUtc >= nowUtc);
            var passed = planned.LastOrDefault(m => m.ScheduledForUtc < nowUtc);

            var from = lastHeld is { } held
                ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(held, zone).DateTime).AddDays(c.EveryDays)
                : today;
            if (c.FirstOnOrAfter is { } first && first > from) from = first;
            if (from <= today) from = today.AddDays(1);
            while ((int)from.DayOfWeek != c.PreferredWeekday % 7) from = from.AddDays(1);
            var proposed = ToUtc(from, c.PreferredLocalTime, zone);

            var state = passed is not null ? "NotRecorded"
                : ahead is not null ? "Planned"
                : from <= today.AddDays(LeadDays(c)) ? "Prepare"
                : "Later";
            result.Add(new MeetingDue(c, state, lastHeld, passed?.ScheduledForUtc ?? ahead?.ScheduledForUtc, proposed));
        }
        return result;
    }

    private static bool Belongs(BdMeetingRow m, MeetingCadenceRow c)
    {
        if (m.CadenceId is { } id) return id == c.Id;
        if (m.Kind != c.MeetingKind) return false;
        return c.MeetingKind switch
        {
            "OneToOne" => string.Equals(m.WithStaffKey, c.WithStaffKey, StringComparison.OrdinalIgnoreCase),
            "AllHands" => true,
            _ => false,
        };
    }

    private static DateTimeOffset ToUtc(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
