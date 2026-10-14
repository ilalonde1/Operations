#nullable enable
using System;
using System.Globalization;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Display the stored wall clock and its explicit offset, independent of the
/// workstation zone. An offset is not a time-zone ID; do not infer DST rules or
/// change ingestion/storage here. Countdown measures elapsed time by instant.
/// </summary>
internal static class OpportunityDeadlineDisplay
{
    internal static string Date(DateTimeOffset? deadline) => deadline is { } d
        ? d.ToString("yyyy-MM-dd HH:mm 'UTC'zzz", CultureInfo.InvariantCulture) + " (stored offset)"
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
        return $"{remaining.Days} day{(remaining.Days == 1 ? "" : "s")} {remaining.Hours} hr remaining";
    }
}
