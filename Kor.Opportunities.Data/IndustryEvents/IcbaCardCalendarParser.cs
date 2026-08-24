#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Data.IndustryEvents;

/// <summary>
/// Parses the ICBA events page (icba.ca/events), a HubSpot CMS page whose
/// calendar is a flat run of <c>&lt;div class="td-content-block"&gt;</c> cells in
/// repeating groups of four: name, date, blurb, venue.
///
/// Verified against the live page 2026-08-24 (10 events, 40 cells). If ICBA
/// restyles the page the groups stop parsing and the source's LastErrorMessage
/// says so, rather than silently writing garbage into IndustryEvents.
/// </summary>
public sealed class IcbaCardCalendarParser : IIndustryEventCalendarParser
{
    public const string Key = "icba-cards";

    private const int FieldsPerCard = 4;

    private static readonly Regex ScriptOrStyle = new(
        @"<(script|style)[^>]*>.*?</\1>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ContentBlock = new(
        @"<div class=""td-content-block"">(.*?)</div>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // "September 15"
    private static readonly Regex SingleDate = new(
        @"^([A-Za-z]{3,9})\.?\s+(\d{1,2})$",
        RegexOptions.Compiled);

    // "Nov 26-27"
    private static readonly Regex SameMonthRange = new(
        @"^([A-Za-z]{3,9})\.?\s+(\d{1,2})\s*[-–]\s*(\d{1,2})$",
        RegexOptions.Compiled);

    // "March 13 - April 2"
    private static readonly Regex CrossMonthRange = new(
        @"^([A-Za-z]{3,9})\.?\s+(\d{1,2})\s*[-–]\s*([A-Za-z]{3,9})\.?\s+(\d{1,2})$",
        RegexOptions.Compiled);

    private static readonly Regex EventLink = new(
        @"href=""(?<url>https?://[^""]*events\.icba\.ca[^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Markets KOR tracks. Used only to label an event's City from its title or
    /// venue string; an unrecognised venue leaves City NULL rather than guessing.
    /// </summary>
    private static readonly string[] KnownCities =
    [
        "Vancouver", "Victoria", "Kelowna", "Calgary", "Edmonton", "Burnaby",
        "Surrey", "Richmond", "Nanaimo", "Kamloops", "Prince George", "Abbotsford",
    ];

    public string ParserKey => Key;

    public IReadOnlyList<ParsedIndustryEvent> Parse(
        string content,
        IndustryEventSourceRow source,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var stripped = ScriptOrStyle.Replace(content, " ");

        var cells = ContentBlock.Matches(stripped)
            .Select(m => CleanText(m.Groups[1].Value))
            .Where(t => t.Length > 0)
            .ToList();

        if (cells.Count < FieldsPerCard)
        {
            return [];
        }

        var links = EventLink.Matches(stripped)
            .Select(m => m.Groups["url"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var events = new List<ParsedIndustryEvent>();

        for (var i = 0; i + FieldsPerCard - 1 < cells.Count; i += FieldsPerCard)
        {
            var name = cells[i];
            var dateText = cells[i + 1];
            var blurb = cells[i + 2];
            var venue = cells[i + 3];

            var parsedDate = ParseDate(dateText, today);
            if (parsedDate is null)
            {
                // Not a card shape we understand — skip this group rather than
                // sliding out of phase across the rest of the page.
                continue;
            }

            var (start, end) = parsedDate.Value;
            var city = DetectCity(name) ?? DetectCity(venue);

            events.Add(new ParsedIndustryEvent(
                Name: name,
                StartDate: start,
                EndDate: end,
                City: city,
                Venue: venue,
                Blurb: blurb,
                RegistrationUrl: MatchLink(links, name, city),
                YearInferred: true));
        }

        return events;
    }

    private static (DateOnly Start, DateOnly? End)? ParseDate(string text, DateOnly today)
    {
        var cross = CrossMonthRange.Match(text);
        if (cross.Success)
        {
            var startMonth = IndustryEventDateResolver.MonthFromName(cross.Groups[1].Value);
            var endMonth = IndustryEventDateResolver.MonthFromName(cross.Groups[3].Value);
            if (startMonth is null || endMonth is null)
            {
                return null;
            }

            var start = IndustryEventDateResolver.ResolveYear(
                startMonth.Value,
                int.Parse(cross.Groups[2].Value),
                today);
            var end = IndustryEventDateResolver.ResolveYear(
                endMonth.Value,
                int.Parse(cross.Groups[4].Value),
                today);

            // A range that wraps the new year: push the end into the next year.
            if (end < start)
            {
                end = end.AddYears(1);
            }

            return (start, end);
        }

        var range = SameMonthRange.Match(text);
        if (range.Success)
        {
            var month = IndustryEventDateResolver.MonthFromName(range.Groups[1].Value);
            if (month is null)
            {
                return null;
            }

            var start = IndustryEventDateResolver.ResolveYear(
                month.Value,
                int.Parse(range.Groups[2].Value),
                today);
            var endDay = Math.Min(
                int.Parse(range.Groups[3].Value),
                DateTime.DaysInMonth(start.Year, start.Month));
            return (start, new DateOnly(start.Year, start.Month, endDay));
        }

        var single = SingleDate.Match(text);
        if (single.Success)
        {
            var month = IndustryEventDateResolver.MonthFromName(single.Groups[1].Value);
            if (month is null)
            {
                return null;
            }

            return (
                IndustryEventDateResolver.ResolveYear(
                    month.Value,
                    int.Parse(single.Groups[2].Value),
                    today),
                null);
        }

        return null;
    }

    private static string? DetectCity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return KnownCities.FirstOrDefault(
            c => text.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    private static string? MatchLink(IReadOnlyList<string> links, string name, string? city)
    {
        if (links.Count == 0)
        {
            return null;
        }

        // Prefer a landing page whose slug names the city (.../2026Victoria).
        if (!string.IsNullOrWhiteSpace(city))
        {
            var byCity = links.FirstOrDefault(
                l => l.Contains(city, StringComparison.OrdinalIgnoreCase));
            if (byCity is not null)
            {
                return byCity;
            }
        }

        // Otherwise a slug that echoes a distinctive word from the title.
        var words = name
            .Split([' ', '-', '&', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 5)
            .ToList();

        return links.FirstOrDefault(
            l => words.Any(w => l.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    private static string CleanText(string html)
    {
        var text = Tag.Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        return Whitespace.Replace(text, " ").Trim();
    }
}
