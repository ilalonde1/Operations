#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Kor.Opportunities.Data.IndustryEvents;

/// <summary>
/// RFC 5545 iCalendar parser, for association calendars that publish a real
/// feed instead of only a web page.
///
/// WHY THIS AND NOT ANOTHER SITE SCRAPER. Seven of the eight event sources sat
/// on ParserKey 'unmapped' because each one looked like its own scraping job.
/// Most of them run The Events Calendar on WordPress, which serves iCal at
/// /events/?ical=1 — one documented format instead of one bespoke reader per
/// association, which is the same reason the ArcGIS and EngagementHQ adapters
/// exist. A new association that publishes iCal becomes a config row.
///
/// ⚠ LINE FOLDING IS THE TRAP. RFC 5545 folds any line over 75 octets onto the
///   next line with a leading space or tab. ACEC-BC's DESCRIPTION runs to about
///   1,800 characters and arrives as roughly thirty folded lines. Parse before
///   unfolding and every long SUMMARY, DESCRIPTION and URL is truncated at the
///   first fold — quietly, because the short ones still look right.
///
/// WHAT THIS COVERS: VEVENT blocks; DTSTART/DTEND in date, local-time and UTC
/// forms, with or without a TZID parameter; the RFC's text escaping; DTEND's
/// exclusive end for all-day events; and property parameters.
///
/// WHAT IT DOES NOT COVER, and a caller should not assume otherwise:
///   * RRULE. A recurring event is read ONCE, at its first occurrence. No
///     association in this set publishes one today, and expanding recurrence
///     correctly is a much larger job than reading a feed.
///   * VTIMEZONE. A TZID is read as a label, not resolved to an offset; dates
///     are taken as written, which is what a calendar page shows a human.
///   * per-event CATEGORIES or ORGANIZER.
/// A same-class fault it would NOT catch: a feed that serves stale events with
/// valid syntax. This reports what the file says, and freshness is the source
/// health checks' job.
/// </summary>
public sealed class IcalCalendarParser : IIndustryEventCalendarParser
{
    public const string Key = "ical";

    private static readonly string[] KnownCities =
    [
        "Vancouver", "Victoria", "Kelowna", "Calgary", "Edmonton", "Burnaby",
        "Surrey", "Richmond", "Nanaimo", "Kamloops", "Prince George", "Abbotsford",
        "New Westminster", "Coquitlam", "Langley", "Whistler", "Penticton", "Vernon",
    ];

    public string ParserKey => Key;

    public IReadOnlyList<ParsedIndustryEvent> Parse(
        string content,
        IndustryEventSourceRow source,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(content) || !content.Contains("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var events = new List<ParsedIndustryEvent>();
        Dictionary<string, string>? current = null;

        foreach (var line in Unfold(content))
        {
            if (line.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (line.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null)
                {
                    var parsed = Build(current, today);
                    if (parsed is not null)
                    {
                        events.Add(parsed);
                    }
                }

                current = null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            // NAME;PARAM=value:content — the property name stops at ';' or ':'.
            var nameAndParams = line[..colon];
            var semi = nameAndParams.IndexOf(';');
            var name = (semi >= 0 ? nameAndParams[..semi] : nameAndParams).Trim();
            var value = line[(colon + 1)..];

            // First wins: a repeated property (an ATTACH per image, say) must
            // not overwrite the one already read.
            if (name.Length > 0 && !current.ContainsKey(name))
            {
                current[name] = value;
            }
        }

        return events;
    }

    private static ParsedIndustryEvent? Build(Dictionary<string, string> p, DateOnly today)
    {
        var name = Unescape(Get(p, "SUMMARY"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var start = ParseDate(Get(p, "DTSTART"));
        if (start is null)
        {
            return null;
        }

        var end = ParseDate(Get(p, "DTEND"));
        var isAllDay = IsDateOnlyValue(Get(p, "DTSTART"));

        // RFC 5545: for an all-day event DTEND is EXCLUSIVE, so a single-day
        // event carries the following day. Left as-is it reports every one-day
        // event as spanning two.
        if (end is not null && isAllDay)
        {
            end = end.Value.AddDays(-1);
            if (end < start)
            {
                end = null;
            }
        }

        if (end == start)
        {
            end = null;
        }

        var location = Unescape(Get(p, "LOCATION"));
        var blurb = Unescape(Get(p, "DESCRIPTION"));

        return new ParsedIndustryEvent(
            Name: Collapse(name),
            StartDate: start.Value,
            EndDate: end,
            City: CityFrom(location) ?? CityFrom(name),
            Venue: string.IsNullOrWhiteSpace(location) ? null : Collapse(location),
            Blurb: string.IsNullOrWhiteSpace(blurb) ? null : Collapse(blurb),
            RegistrationUrl: NullIfBlank(Get(p, "URL")),
            // The feed states a full date; nothing is inferred.
            YearInferred: false);
    }

    /// <summary>
    /// RFC 5545 §3.1: a line beginning with a space or tab continues the one
    /// before it, and the leading whitespace character is dropped.
    /// </summary>
    internal static IEnumerable<string> Unfold(string content)
    {
        var sb = new StringBuilder();
        var started = false;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                sb.Append(line[1..]);
                continue;
            }

            if (started)
            {
                yield return sb.ToString();
            }

            sb.Clear();
            sb.Append(line);
            started = true;
        }

        if (started)
        {
            yield return sb.ToString();
        }
    }

    private static bool IsDateOnlyValue(string? v)
        => v is not null && v.Trim().Length == 8 && v.Trim().All(char.IsDigit);

    private static DateOnly? ParseDate(string? v)
    {
        if (string.IsNullOrWhiteSpace(v))
        {
            return null;
        }

        var s = v.Trim();

        // 20261020T150000, 20261020T150000Z, or 20261020.
        var t = s.IndexOf('T');
        var datePart = t > 0 ? s[..t] : s;
        if (datePart.Length != 8 || !datePart.All(char.IsDigit))
        {
            return null;
        }

        return DateOnly.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    private static string? Get(Dictionary<string, string> p, string key)
        => p.TryGetValue(key, out var v) ? v : null;

    /// <summary>RFC 5545 §3.3.11 text escaping.</summary>
    internal static string Unescape(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        return s.Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("\\,", ",", StringComparison.Ordinal)
                .Replace("\\;", ";", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastSpace = false;
        foreach (var c in s)
        {
            var isSpace = char.IsWhiteSpace(c);
            if (isSpace)
            {
                if (!lastSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(c);
            }

            lastSpace = isSpace;
        }

        return sb.ToString().Trim();
    }

    private static string? CityFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Longest first, so "New Westminster" is not claimed by a shorter token.
        return KnownCities
            .OrderByDescending(c => c.Length)
            .FirstOrDefault(c => text.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
