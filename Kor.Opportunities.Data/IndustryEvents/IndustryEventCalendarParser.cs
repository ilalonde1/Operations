#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Data.IndustryEvents;

/// <summary>One event as lifted off an association calendar, before enrichment.</summary>
public sealed record ParsedIndustryEvent(
    string Name,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? City,
    string? Venue,
    string? Blurb,
    string? RegistrationUrl,
    bool YearInferred);

public interface IIndustryEventCalendarParser
{
    /// <summary>Matches <c>IndustryEventSource.ParserKey</c>.</summary>
    string ParserKey { get; }

    IReadOnlyList<ParsedIndustryEvent> Parse(string content, IndustryEventSourceRow source, DateOnly today);
}

/// <summary>
/// Shared date helpers. Association calendars routinely omit the year
/// ("September 15"), so a year has to be inferred.
/// </summary>
public static class IndustryEventDateResolver
{
    /// <summary>
    /// How far into the past a bare month/day is still read as "this year"
    /// rather than rolled forward. Without the grace window an event that
    /// finished yesterday would be recorded as happening in 11 months.
    /// </summary>
    public const int GraceDays = 30;

    public static DateOnly ResolveYear(int month, int day, DateOnly today)
    {
        var candidate = SafeDate(today.Year, month, day);
        if (candidate >= today.AddDays(-GraceDays))
        {
            return candidate;
        }

        return SafeDate(today.Year + 1, month, day);
    }

    private static DateOnly SafeDate(int year, int month, int day)
    {
        // Feb 29 on a common year: fall back to the 28th rather than throwing.
        var maxDay = DateTime.DaysInMonth(year, month);
        return new DateOnly(year, month, Math.Min(day, maxDay));
    }

    public static int? MonthFromName(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return token.Trim().TrimEnd('.').ToLowerInvariant() switch
        {
            "jan" or "january" => 1,
            "feb" or "february" => 2,
            "mar" or "march" => 3,
            "apr" or "april" => 4,
            "may" => 5,
            "jun" or "june" => 6,
            "jul" or "july" => 7,
            "aug" or "august" => 8,
            "sep" or "sept" or "september" => 9,
            "oct" or "october" => 10,
            "nov" or "november" => 11,
            "dec" or "december" => 12,
            _ => null,
        };
    }
}
