#nullable enable
using System;

namespace Kor.Opportunities.Data.IndustryEvents;

/// <summary>
/// One association calendar the worker polls for industry events.
/// Rows live in <c>opportunities.IndustryEventSource</c> — the source list is
/// database state, not a checked-in document.
/// </summary>
public sealed record IndustryEventSourceRow(
    long Id,
    string Name,
    string Organizer,
    string CalendarUrl,
    string? SiteUrl,
    string ParserKey,
    string? Region,
    string? DefaultMarket,
    string? DefaultEventType,
    string? KorRelevance,
    bool IsActive,
    int CrawlDelaySeconds,
    DateTimeOffset? LastPolledAtUtc,
    string? LastErrorMessage,
    int? LastEventCount);

/// <summary>
/// The subset of <see cref="IndustryEventSourceRow"/> an operator (or the
/// bootstrap service) supplies when registering a calendar.
/// </summary>
public sealed record IndustryEventSourceSeed(
    string Name,
    string Organizer,
    string CalendarUrl,
    string ParserKey,
    string? SiteUrl = null,
    string? Region = null,
    string? DefaultMarket = null,
    string? DefaultEventType = null,
    string? KorRelevance = null,
    bool IsActive = true,
    int CrawlDelaySeconds = 86400);
