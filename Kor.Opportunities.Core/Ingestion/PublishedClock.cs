#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// Turns a timestamp AS A SOURCE PUBLISHED IT into a real UTC instant.
/// </summary>
/// <remarks>
/// <para>
/// Every ingestion parse site used to pass
/// <c>DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal</c>.
/// That flag pair is correct only when the published string carries its own
/// numeric offset (SamGov's <c>2025-05-01T12:00:00-07:00</c>) or is genuinely
/// UTC (LA City RAMP's <c>closedate</c>). When a portal publishes a LOCAL wall
/// clock — "Wed Oct 21, 2026 2:00:00 PM (MDT)", or a BC Bid grid column headed
/// "closing date and time (pacific time)" — AssumeUniversal relabels that wall
/// clock as UTC and the stored instant lands early by the zone's offset:
/// measured 2026-10-13 as 2,010 of 2,377 deadline-bearing opportunities, 1,757
/// of them by 7 hours.
/// </para>
/// <para>
/// So a provider must say which clock it is reading, and there is no default.
/// <see cref="TryFromNamedZone"/> is for text that names its own zone;
/// <see cref="TryFromLocalWallClock"/> is for a source whose zone is a property
/// of the portal rather than of the row.
/// </para>
/// <para>
/// An ambiguous or non-existent local time (the hour that repeats or the hour
/// that does not exist at a DST transition) is REJECTED rather than guessed.
/// A closing time inside that window is a portal defect worth seeing, and
/// silently picking one of two instants is how this class of bug started.
/// </para>
/// </remarks>
public static class PublishedClock
{
    /// <summary>IANA ids with Windows fallbacks — the fallback matters on hosts
    /// whose ICU data is absent, where FindSystemTimeZoneById("America/...")
    /// throws.</summary>
    private static readonly Dictionary<string, string[]> ZoneAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["America/Vancouver"] = new[] { "America/Vancouver", "Pacific Standard Time" },
        ["America/Los_Angeles"] = new[] { "America/Los_Angeles", "Pacific Standard Time" },
        ["America/Edmonton"] = new[] { "America/Edmonton", "Mountain Standard Time" },
        ["America/Winnipeg"] = new[] { "America/Winnipeg", "Central Standard Time" },
        ["America/Toronto"] = new[] { "America/Toronto", "Eastern Standard Time" },
        ["America/Halifax"] = new[] { "America/Halifax", "Atlantic Standard Time" },
        ["America/St_Johns"] = new[] { "America/St_Johns", "Newfoundland Standard Time" },
    };

    /// <summary>
    /// North American zone abbreviations as Canadian and US procurement portals
    /// render them. A D/S abbreviation still resolves through the zone's own DST
    /// rule for that date rather than a fixed offset: where the two disagree the
    /// portal's own clock is what bidders act on, and a mismatched abbreviation
    /// is a portal bug we must not encode.
    /// </summary>
    private static readonly Dictionary<string, string> AbbreviationZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PST"] = "America/Vancouver", ["PDT"] = "America/Vancouver", ["PT"] = "America/Vancouver",
        ["MST"] = "America/Edmonton", ["MDT"] = "America/Edmonton", ["MT"] = "America/Edmonton",
        ["CST"] = "America/Winnipeg", ["CDT"] = "America/Winnipeg", ["CT"] = "America/Winnipeg",
        ["EST"] = "America/Toronto", ["EDT"] = "America/Toronto", ["ET"] = "America/Toronto",
        ["AST"] = "America/Halifax", ["ADT"] = "America/Halifax", ["AT"] = "America/Halifax",
        ["NST"] = "America/St_Johns", ["NDT"] = "America/St_Johns",
    };

    // A trailing zone abbreviation, parenthesised or bare:
    //   "Wed Oct 21, 2026 2:00:00 PM (MDT)"      bids&tenders
    //   "Nov 02, 2026 2:00 PM PST"               Bonfire RSS prose
    //   "Oct 23, 2026 . 4:00:00 pm (MT)"         Alberta Purchasing Connection
    private static readonly Regex TrailingZoneRegex = new(
        @"[\s(]*\b(?<abbr>P[SD]T|M[SD]T|C[SD]T|E[SD]T|A[SD]T|N[SD]T|PT|MT|CT|ET|AT)\b\)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Resolves a zone id, trying its Windows alias before giving up. Returns
    /// null rather than throwing: a source that cannot resolve its zone must
    /// yield no timestamp, which is a handled outcome, instead of a wrong one.
    /// </summary>
    public static TimeZoneInfo? ResolveZone(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return null;
        }

        var candidates = ZoneAliases.TryGetValue(zoneId, out var aliases)
            ? aliases
            : new[] { zoneId };

        foreach (var candidate in candidates)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return null;
    }

    /// <summary>Maps a zone abbreviation such as PDT or MT to its IANA id.</summary>
    public static string? ZoneIdForAbbreviation(string? abbreviation)
        => !string.IsNullOrWhiteSpace(abbreviation)
            && AbbreviationZones.TryGetValue(abbreviation.Trim(), out var zone)
            ? zone
            : null;

    /// <summary>
    /// Parses text that names its own zone, e.g.
    /// <c>"Wed Oct 21, 2026 2:00:00 PM (MDT)"</c> or
    /// <c>"Nov 02, 2026 2:00 PM PST"</c>.
    /// </summary>
    /// <returns>
    /// False when the text carries no recognised zone abbreviation, so a caller
    /// can fall back deliberately instead of inheriting a silent UTC assumption.
    /// </returns>
    public static bool TryFromNamedZone(string? text, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = TrailingZoneRegex.Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var zoneId = ZoneIdForAbbreviation(match.Groups["abbr"].Value);
        if (zoneId is null)
        {
            return false;
        }

        // Everything before the abbreviation is the wall clock. Parse it with
        // DateTimeStyles.None: no assumption either way, so a string that
        // actually carries an offset is rejected here and handled by the caller.
        var clockText = text.Trim()[..match.Index].Trim().TrimEnd('(', ',', '.', '·', '•');
        if (!DateTime.TryParse(clockText, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local)
            || local.Kind != DateTimeKind.Unspecified)
        {
            return false;
        }

        return TryFromLocalWallClock(local, zoneId, out result);
    }

    /// <summary>
    /// Converts a wall clock the source published in <paramref name="zoneId"/>
    /// into a UTC instant.
    /// </summary>
    /// <returns>
    /// False when the zone cannot be resolved, or when the wall clock is
    /// ambiguous or non-existent in that zone.
    /// </returns>
    public static bool TryFromLocalWallClock(DateTime local, string? zoneId, out DateTimeOffset result)
        => TryFromLocalWallClock(local, ResolveZone(zoneId), out result);

    /// <inheritdoc cref="TryFromLocalWallClock(DateTime, string, out DateTimeOffset)"/>
    public static bool TryFromLocalWallClock(DateTime local, TimeZoneInfo? zone, out DateTimeOffset result)
    {
        result = default;
        if (zone is null)
        {
            return false;
        }

        // A value that already carries a kind is not a wall clock; converting it
        // would double-apply an offset.
        var naive = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(naive) || zone.IsAmbiguousTime(naive))
        {
            return false;
        }

        // KEEP THE SOURCE'S OFFSET rather than normalising to +00:00. The instant
        // is identical either way -- DateTimeOffset comparison and equality are by
        // instant, and SQL Server's datetimeoffset compares the same way -- but
        // keeping the offset leaves the PUBLISHED LOCAL DATE recoverable via
        // .Date, and that date is a field in its own right: IngestionService
        // derives Opportunity.RfpReleaseDate from the posted date. Normalising to
        // UTC first loses it, and a posting issued at 5pm Pacific or later would
        // get a release date one day late (107 BC Bid observations sit at hour
        // 17-23 local, so this is not hypothetical).
        var utc = TimeZoneInfo.ConvertTimeToUtc(naive, zone);
        result = new DateTimeOffset(naive, zone.GetUtcOffset(utc));
        return true;
    }

    /// <summary>
    /// True when a published value carries a date but no time of day, so any
    /// time in it was supplied by the parser rather than the publisher.
    /// </summary>
    /// <remarks>
    /// "Posting expires: Wednesday, October 28, 2026" has no closing time. Stored
    /// as midnight it reads as 17:00 on the 27th in Pacific, and
    /// <c>DataRetirementJob</c> expires on
    /// <c>SubmissionDeadlineUtc &lt; SYSDATETIMEOFFSET()</c> -- so the whole final
    /// day is lost and the opportunity disappears before it closes.
    /// <see cref="EndOfPublishedDay"/> is how a date-only deadline should be
    /// stored: the last moment of that day in the source's own zone. It errs
    /// toward keeping an opportunity visible, which is the right direction for a
    /// radar, and it invents no closing hour the publisher did not give.
    /// </remarks>
    public static bool IsDateOnly(DateTimeOffset value)
        => value.TimeOfDay == TimeSpan.Zero;

    /// <summary>
    /// Turns a date-only deadline into the last second of that published day in
    /// <paramref name="zoneId"/>. Returns the value unchanged when the zone
    /// cannot be resolved or the day is not representable there.
    /// </summary>
    public static DateTimeOffset EndOfPublishedDay(DateTimeOffset value, string? zoneId)
    {
        var zone = ResolveZone(zoneId);
        if (zone is null)
        {
            return value;
        }

        var endOfDay = value.Date.AddDays(1).AddSeconds(-1);
        return TryFromLocalWallClock(endOfDay, zone, out var converted) ? converted : value;
    }

    /// <summary>
    /// The parse a tender source should use: honour an offset the string
    /// carries, else a zone it names, else treat it as a wall clock in
    /// <paramref name="fallbackZoneId"/> — the zone the PORTAL publishes in.
    /// Pass null for <paramref name="fallbackZoneId"/> only for a feed proven to
    /// publish UTC.
    /// </summary>
    public static DateTimeOffset? Parse(string? text, string? fallbackZoneId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        if (TryFromNamedZone(trimmed, out var named))
        {
            return named;
        }

        // An explicit numeric offset or a Z suffix is authoritative. Round-trip
        // style flags are deliberately absent: DateTimeOffset.Parse without
        // AssumeUniversal treats an offset-less string as machine-local, so the
        // offset-less case is routed below instead of being guessed at here.
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var withOffset)
            && CarriesExplicitOffset(trimmed))
        {
            return withOffset.ToUniversalTime();
        }

        if (!DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
        {
            return null;
        }

        if (fallbackZoneId is null)
        {
            // Proven-UTC feed.
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc));
        }

        return TryFromLocalWallClock(local, fallbackZoneId, out var converted)
            ? converted
            : (DateTimeOffset?)null;
    }

    /// <summary>
    /// <see cref="Parse"/> for a CLOSING date: identical, except that a value
    /// carrying no time of day becomes the last second of that published day in
    /// <paramref name="fallbackZoneId"/> instead of its first.
    /// </summary>
    /// <remarks>
    /// Use this for a deadline and <see cref="Parse"/> for a posted date. The
    /// difference matters in one direction only: a deadline stored at the START
    /// of its day expires before the day it names, whereas a posted date stored
    /// at the start of its day is simply that date.
    /// </remarks>
    public static DateTimeOffset? ParseDeadline(string? text, string? fallbackZoneId)
    {
        var parsed = Parse(text, fallbackZoneId);
        return parsed is { } value && IsDateOnly(value)
            ? EndOfPublishedDay(value, fallbackZoneId)
            : parsed;
    }

    // "2026-05-01T12:00:00-07:00" / "...Z" carry an offset; "2026-05-01T12:00:00"
    // and "May 1, 2026 12:00 PM" do not. Checked on the tail so a date like
    // "2026-05-01" is not mistaken for an offset by its own hyphens.
    private static readonly Regex ExplicitOffsetRegex = new(
        @"(?:Z|[+\-]\d{2}:?\d{2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool CarriesExplicitOffset(string text)
        => ExplicitOffsetRegex.IsMatch(text.Trim());
}
