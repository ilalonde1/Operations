#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Crm;

/// <summary>One numbered line of an owner's reply to a roll-up: "2. moved to Fri 30 Oct". Kind is Done, Moved, Dropped or Unclear.</summary>
public sealed record RollupReplyLine(int Number, string Kind, DateOnly? MovedTo, string? Note, string Raw);

/// <summary>One item of a roll-up: its number, the pursuit it covers and the touchstone it asked about.</summary>
public sealed record RollupItem(int Number, long EngagementId, int TouchstoneId);

/// <summary>What saving one reply line will do to one pursuit, worded for the preview Ian confirms.</summary>
public sealed record RollupReplyAction(RollupReplyLine Line, RollupItem Item, string Title, string Preview);

/// <summary>
/// Owners answer a roll-up with one line per number: "done", "moved to &lt;date&gt;", or "dropped and why" (the roll-up's own
/// closing line, MorningPromptComposer). This reads those lines, maps each number to the pursuit the roll-up numbered, and
/// works out the change: every answered line is logged as an activity on its pursuit (which also restarts the keep-warm
/// clock), "moved" sets the next step's date, "done" on a due next step clears it, and "dropped" archives the pursuit with the
/// reason. Pure; the Director previews the actions, Ian confirms, and only then are they written through the CRM stores.
/// Numbers follow the order of the roll-up's touchstone keys, which the composer prints in item order and the import checks
/// against the count of numbered blocks; the preview names each pursuit so a mismatch is visible before anything is saved.
/// </summary>
public static class RollupReply
{
    private static readonly Regex Numbered = new(@"^\s*(\d{1,2})\s*[.):\-]\s*(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex DoneWord = new(@"^(?:done|complete[d]?|did it|sent|called|met|yes)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MovedWord = new(@"^(?:moved?|push(?:ed)?|delay(?:ed)?|resched(?:uled)?|postponed?|next week|by\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DroppedWord = new(@"^(?:drop(?:ped)?|dead|lost|no longer|not pursuing|kill(?:ed)?|gone)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] DayNames = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

    /// <summary>The roll-up's items: one per pursuit, numbered in the order of the draft's touchstone keys.</summary>
    public static IReadOnlyList<RollupItem> Items(HourDraft draft)
    {
        var items = new List<RollupItem>();
        foreach (var raw in draft.Touchstones ?? Array.Empty<string>())
            if (TouchstoneKey.TryParse(raw, out var key) && items.All(i => i.EngagementId != key.EngagementId))
                items.Add(new RollupItem(items.Count + 1, key.EngagementId, key.TouchstoneId));
        return items;
    }

    /// <summary>Reads the numbered lines of a reply. Quoted text (lines starting "&gt;") and un-numbered lines are ignored.</summary>
    public static IReadOnlyList<RollupReplyLine> Parse(string reply, DateOnly today)
    {
        var lines = new List<RollupReplyLine>();
        foreach (var raw in (reply ?? "").Replace("\r", "").Split('\n'))
        {
            if (raw.TrimStart().StartsWith('>')) continue;
            var m = Numbered.Match(raw);
            if (!m.Success) continue;
            var number = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var text = m.Groups[2].Value.Trim();
            var lead = text.TrimStart('-', '–', '—', ' ');
            if (DroppedWord.IsMatch(lead))
                lines.Add(new RollupReplyLine(number, "Dropped", null, Rest(lead, DroppedWord), raw.Trim()));
            else if (DoneWord.IsMatch(lead))
                lines.Add(new RollupReplyLine(number, "Done", null, Rest(lead, DoneWord), raw.Trim()));
            else if (MovedWord.IsMatch(lead) && FindDate(lead, today) is { } date)
                lines.Add(new RollupReplyLine(number, "Moved", date, null, raw.Trim()));
            else
                lines.Add(new RollupReplyLine(number, "Unclear", null, null, raw.Trim()));
        }
        return lines;
    }

    /// <summary>
    /// Pairs each understood line with its item. Problems (a number the roll-up does not have, a number answered twice, a line
    /// not understood, a "moved" date in the past) are returned for the preview and nothing is planned for them.
    /// </summary>
    public static (IReadOnlyList<RollupReplyAction> Actions, IReadOnlyList<string> Problems) Plan(IReadOnlyList<RollupReplyLine> lines,
        IReadOnlyList<RollupItem> items, Func<long, string> title, DateOnly today)
    {
        var actions = new List<RollupReplyAction>();
        var problems = new List<string>();
        foreach (var group in lines.GroupBy(l => l.Number))
        {
            var line = group.First();
            if (group.Count() > 1) { problems.Add($"{line.Number} is answered {group.Count()} times; only the first is used."); }
            var item = items.FirstOrDefault(i => i.Number == line.Number);
            if (item is null) { problems.Add($"{line.Number}: the roll-up has no item {line.Number} (it has {items.Count})."); continue; }
            var name = title(item.EngagementId);
            switch (line.Kind)
            {
                case "Unclear":
                    problems.Add($"{line.Number} ({name}): not understood, so not saved: \"{line.Raw}\".");
                    continue;
                case "Moved" when line.MovedTo is { } d && d < today:
                    problems.Add($"{line.Number} ({name}): moved to {Day(d)}, which has passed; not saved.");
                    continue;
            }
            actions.Add(new RollupReplyAction(line, item, name, line.Kind switch
            {
                "Done" => $"{line.Number}. {name}: done{(item.TouchstoneId == NextActionTouchstone ? "; its due next step is cleared" : "")}; logged.",
                "Moved" => $"{line.Number}. {name}: next step moved to {Day(line.MovedTo!.Value)}; logged.",
                _ => $"{line.Number}. {name}: dropped, so the pursuit is archived{(line.Note is { } n ? " (" + n + ")" : "")}; logged.",
            }));
        }
        var unanswered = items.Where(i => lines.All(l => l.Number != i.Number)).Select(i => i.Number).ToList();
        if (unanswered.Count > 0) problems.Add("Not answered: " + string.Join(", ", unanswered) + ".");
        return (actions.OrderBy(a => a.Item.Number).ToList(), problems);
    }

    /// <summary>The touchstone rule "Your next step is due" (migration 371).</summary>
    public const int NextActionTouchstone = 9;

    /// <summary>The pursuit after the reply line: unchanged for a plain "done" on anything but a due next step.</summary>
    public static CrmEngagement Apply(CrmEngagement e, RollupReplyAction a, TimeZoneInfo zone) => a.Line.Kind switch
    {
        "Moved" => e with { NextActionDueUtc = EndOfDay(a.Line.MovedTo!.Value, zone) },
        "Done" when a.Item.TouchstoneId == NextActionTouchstone => e with { NextActionDueUtc = null, NextActionNote = null },
        "Dropped" => e with { Stage = CrmEngagementStage.Archived, OutcomeReason = a.Line.Note ?? "Dropped in a roll-up reply" },
        _ => e,
    };

    /// <summary>The activity every saved line leaves on its pursuit, so the reply is on record and the keep-warm clock restarts.</summary>
    public static CrmActivity Activity(RollupReplyAction a, string owner, string actor, DateTimeOffset now) => new()
    {
        EngagementId = a.Item.EngagementId, ActivityType = CrmActivityType.Note, OccurredAtUtc = now,
        Subject = $"Roll-up reply from {owner}: {a.Line.Kind.ToLowerInvariant()}", Body = a.Line.Raw, CreatedBy = actor,
    };

    public static string Day(DateOnly d) => d.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    private static DateTimeOffset EndOfDay(DateOnly d, TimeZoneInfo zone)
    {
        var local = d.ToDateTime(new TimeOnly(17, 0));
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static string? Rest(string text, Regex word)
    {
        var rest = word.Replace(text, "", 1).Trim().TrimStart(',', ';', ':', '-', '–', '—', '.').Trim();
        if (rest.StartsWith("and ", StringComparison.OrdinalIgnoreCase)) rest = rest[4..].Trim();
        if (rest.StartsWith("because ", StringComparison.OrdinalIgnoreCase)) rest = rest[8..].Trim();
        return rest.Length == 0 ? null : rest;
    }

    /// <summary>A date in a reply: 2026-10-30, 30 Oct, Oct 30, Fri 30 Oct, Friday (the next one), or "next week" (a week out).</summary>
    public static DateOnly? FindDate(string text, DateOnly today)
    {
        var iso = Regex.Match(text, @"\b(20\d{2})-(\d{1,2})-(\d{1,2})\b");
        if (iso.Success && DateOnly.TryParseExact(iso.Value, new[] { "yyyy-M-d", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return d1;
        var dm = Regex.Match(text, @"\b(\d{1,2})\s+(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\b", RegexOptions.IgnoreCase);
        if (dm.Success) return InYear(int.Parse(dm.Groups[1].Value, CultureInfo.InvariantCulture), dm.Groups[2].Value, today);
        var md = Regex.Match(text, @"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\s+(\d{1,2})\b", RegexOptions.IgnoreCase);
        if (md.Success) return InYear(int.Parse(md.Groups[2].Value, CultureInfo.InvariantCulture), md.Groups[1].Value, today);
        if (Regex.IsMatch(text, @"\bnext week\b", RegexOptions.IgnoreCase)) return today.AddDays(7);
        var day = Regex.Match(text, @"\b(sun|mon|tue|wed|thu|fri|sat)[a-z]*\b", RegexOptions.IgnoreCase);
        if (day.Success)
        {
            var target = Array.FindIndex(DayNames, n => n.StartsWith(day.Groups[1].Value.ToLowerInvariant(), StringComparison.Ordinal));
            var ahead = ((target - (int)today.DayOfWeek) + 7) % 7;
            return today.AddDays(ahead == 0 ? 7 : ahead);
        }
        return null;
    }

    private static DateOnly? InYear(int day, string month, DateOnly today)
    {
        var m = Array.FindIndex(CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames,
            n => n.Length > 0 && n.StartsWith(month[..3], StringComparison.OrdinalIgnoreCase)) + 1;
        if (m < 1 || day < 1 || day > DateTime.DaysInMonth(today.Year, m)) return null;
        var date = new DateOnly(today.Year, m, day);
        return date < today.AddDays(-60) ? date.AddYears(1) : date; // "5 Jan" written in December means next January
    }
}
