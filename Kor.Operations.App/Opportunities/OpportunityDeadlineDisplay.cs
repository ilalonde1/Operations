#nullable enable
using System;
using System.Globalization;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Deadlines on screen and in briefs: the reader's local wall clock, in words a person
/// says ("Mon Oct 19, 2:00 PM"), with the countdown measured by instant. Until
/// 2026-10-14 every list showed the stored value with a developer's caveat appended,
/// "2026-10-19 21:00 UTC+00:00" and the words "stored offset", on every row of every
/// list. The stored wall clock and offset remain available through <see cref="Stored"/>
/// for the editor and for tooltips. Nothing here touches ingestion or storage; a wrong
/// stored instant stays wrong, it just reads as a time instead of a dump. The zone is
/// the workstation's (TimeZoneInfo.Local). KOR's readers are in the Pacific zone, so an
/// Alberta 2:00 PM close shows as 1:00 PM, which is when it closes for them. The year
/// appears only when it is not the current year, judged by the real clock in every
/// consumer, so a row, a converter and a brief render the same deadline identically.
/// </summary>
internal static class OpportunityDeadlineDisplay
{
    private const string ThisYear = "ddd MMM d, h:mm tt";
    private const string OtherYear = "ddd MMM d yyyy, h:mm tt";

    internal static string Date(DateTimeOffset? deadline)
    {
        if (deadline is not { } d)
        {
            return "not specified";
        }

        var local = d.ToLocalTime();
        var pattern = local.Year == DateTimeOffset.Now.Year ? ThisYear : OtherYear;
        return local.ToString(pattern, CultureInfo.InvariantCulture);
    }

    /// <summary>The value as stored: wall clock and explicit offset. For the record editor
    /// (which edits the date in that offset) and for tooltips; never for a list.</summary>
    internal static string Stored(DateTimeOffset? deadline) => deadline is { } d
        ? d.ToString("yyyy-MM-dd HH:mm 'UTC'zzz", CultureInfo.InvariantCulture)
        : "not specified";

    internal static string Format(DateTimeOffset? deadline, DateTimeOffset now) => deadline is { } d
        ? Date(d) + " — " + Countdown(d, now)
        : "not specified";

    internal static string Countdown(DateTimeOffset deadline, DateTimeOffset now)
    {
        var remaining = deadline - now;
        if (remaining < TimeSpan.Zero) return "expired";
        if (remaining == TimeSpan.Zero) return "due now";
        if (remaining.TotalMinutes < 1) return "less than 1 min remaining";
        if (remaining.TotalHours < 1) return $"{(int)remaining.TotalMinutes} min remaining";
        if (remaining.TotalDays < 1) return $"{remaining.Hours} hr {remaining.Minutes} min remaining";
        if (remaining.TotalDays < 2) return $"1 day {remaining.Hours} hr remaining";
        return $"{remaining.Days} days remaining";   // "26 days 0 hr" said nothing the days did not
    }
}
