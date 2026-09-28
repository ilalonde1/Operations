#nullable enable
using System;
using System.Linq;
using Kor.Opportunities.Data.IndustryEvents;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// WHAT THESE COVER: the four things an iCalendar feed does that break a naive
/// reader, each taken from the real ACEC-BC feed fetched on 2026-09-28 rather
/// than written from memory.
///
///  1. VTIMEZONE carries its own DTSTART lines. ACEC-BC's feed has FOUR of them
///     — daylight and standard transitions for 2025, 2026 and 2027 — before the
///     first real event. A reader that scans for DTSTART instead of tracking
///     BEGIN:VEVENT invents four events out of timezone rules.
///  2. Line folding (RFC 5545 §3.1). ACEC-BC does not fold, so this is proved
///     against a synthetic case; without it every long DESCRIPTION is truncated
///     at the first fold, quietly, because short fields still look right.
///  3. Text escaping. "Burnaby\, Coquitlam" is one summary, not two fields.
///  4. DTEND is EXCLUSIVE for all-day events, so a one-day event carries the
///     following day and reports as two days long if taken literally.
///
/// WHAT THEY DO NOT COVER: RRULE (a recurring event is read once, at its first
/// occurrence), VTIMEZONE offset resolution (TZID is a label here), and whether
/// the feed is fresh. A same-class fault they would NOT catch: a feed serving
/// last year's events with perfect syntax — that is the source health checks'
/// job, not the parser's.
/// </summary>
public sealed class IcalCalendarParserTests
{
    private static readonly IndustryEventSourceRow Source = new(
        Id: 1, Name: "ACEC-BC", Organizer: "ACEC-BC",
        CalendarUrl: "https://www.acec-bc.ca/events/?ical=1", SiteUrl: null,
        ParserKey: IcalCalendarParser.Key, Region: "CA-BC", DefaultMarket: "British Columbia",
        DefaultEventType: "association", KorRelevance: null, IsActive: true,
        CrawlDelaySeconds: 86400, LastPolledAtUtc: null, LastErrorMessage: null,
        LastEventCount: null);

    private static readonly DateOnly Today = new(2026, 9, 28);

    // Abridged from the live feed. The VTIMEZONE block is kept verbatim because
    // its DTSTART lines are the trap in test 1.
    private const string AcecFeed = """
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//ACEC-BC//The Events Calendar//EN
BEGIN:VTIMEZONE
TZID:America/Vancouver
BEGIN:DAYLIGHT
DTSTART:20260308T100000
TZOFFSETFROM:-0800
TZOFFSETTO:-0700
END:DAYLIGHT
BEGIN:STANDARD
DTSTART:20261101T090000
TZOFFSETFROM:-0700
TZOFFSETTO:-0800
END:STANDARD
END:VTIMEZONE
BEGIN:VEVENT
DTSTART;TZID=America/Vancouver:20261020T150000
DTEND;TZID=America/Vancouver:20261020T180000
UID:10000146-1792508400-1792519200@acec-bc.ca
SUMMARY:Client Engagement Event with Burnaby\, Coquitlam\, & New West
DESCRIPTION:Join ACEC-BC for an evening of engagement\, discussion\, and networking.
URL:https://acec-bc.ca/event/client-engagement-event-with-burnaby-coquitlam-new-west/
LOCATION:Online + Anvil Centre\, 777 Columbia St\, New Westminster\, BC\, V3M 1B6\, Canada
END:VEVENT
BEGIN:VEVENT
DTSTART;TZID=America/Vancouver:20261105T080000
DTEND;TZID=America/Vancouver:20261105T193000
SUMMARY:2026 Future Leaders Network Conference
URL:https://acec-bc.ca/event/2026-future-leaders-network-conference/
LOCATION:Online + BCIT Vancouver Tech Collider\, 555 Seymour Street\, Vancouver\, BC\, Canada
END:VEVENT
BEGIN:VEVENT
DTSTART;TZID=America/Vancouver:20270203T080000
DTEND;TZID=America/Vancouver:20270204T200000
SUMMARY:Transportation Conference 2027
URL:https://acec-bc.ca/event/transportation-conference-2027/
LOCATION:Online + Fairmont Hotel Vancouver\, 900 West Georgia Street\, Vancouver\, BC\, Canada
END:VEVENT
END:VCALENDAR
""";

    private static IcalCalendarParser Parser() => new();

    [Fact]
    public void TimezoneTransitionsAreNotEvents()
    {
        var events = Parser().Parse(AcecFeed, Source, Today);

        // Two DTSTART lines live inside VTIMEZONE. Three VEVENTs exist.
        Assert.Equal(3, events.Count);
        Assert.DoesNotContain(events, e => e.StartDate == new DateOnly(2026, 3, 8));
        Assert.DoesNotContain(events, e => e.StartDate == new DateOnly(2026, 11, 1));
    }

    [Fact]
    public void TheRealEventsComeThroughWithTheirDatesAndLinks()
    {
        var events = Parser().Parse(AcecFeed, Source, Today);

        var first = events.Single(e => e.Name.StartsWith("Client Engagement", StringComparison.Ordinal));
        Assert.Equal(new DateOnly(2026, 10, 20), first.StartDate);
        Assert.Null(first.EndDate); // same-day event
        Assert.Equal("https://acec-bc.ca/event/client-engagement-event-with-burnaby-coquitlam-new-west/",
            first.RegistrationUrl);
        Assert.Equal("New Westminster", first.City);

        var conf = events.Single(e => e.Name == "Transportation Conference 2027");
        Assert.Equal(new DateOnly(2027, 2, 3), conf.StartDate);
        Assert.Equal(new DateOnly(2027, 2, 4), conf.EndDate);
        Assert.Equal("Vancouver", conf.City);
    }

    [Fact]
    public void EscapedCommasAreOneSummaryNotThreeFields()
    {
        var events = Parser().Parse(AcecFeed, Source, Today);

        Assert.Contains(events,
            e => e.Name == "Client Engagement Event with Burnaby, Coquitlam, & New West");
    }

    [Fact]
    public void AFoldedLineIsRejoinedBeforeItIsParsed()
    {
        // RFC 5545 folds at 75 octets with a leading space. ACEC-BC does not
        // fold, so this is the synthetic proof: unfolded, the summary would stop
        // at "Annual" and the URL would lose its tail.
        const string Folded = """
BEGIN:VCALENDAR
BEGIN:VEVENT
DTSTART;VALUE=DATE:20261110
SUMMARY:Annual
  General Meeting and Awards Banquet
URL:https://example.test/events/
 agm-2026
END:VEVENT
END:VCALENDAR
""";
        var e = Assert.Single(Parser().Parse(Folded, Source, Today));

        Assert.Equal("Annual General Meeting and Awards Banquet", e.Name);
        Assert.Equal("https://example.test/events/agm-2026", e.RegistrationUrl);
    }

    [Fact]
    public void AllDayDtEndIsExclusiveSoASingleDayEventHasNoEndDate()
    {
        const string AllDay = """
BEGIN:VCALENDAR
BEGIN:VEVENT
DTSTART;VALUE=DATE:20261110
DTEND;VALUE=DATE:20261111
SUMMARY:One Day Workshop
END:VEVENT
BEGIN:VEVENT
DTSTART;VALUE=DATE:20261201
DTEND;VALUE=DATE:20261204
SUMMARY:Three Day Conference
END:VEVENT
END:VCALENDAR
""";
        var events = Parser().Parse(AllDay, Source, Today);

        var one = events.Single(e => e.Name == "One Day Workshop");
        Assert.Equal(new DateOnly(2026, 11, 10), one.StartDate);
        Assert.Null(one.EndDate);

        var three = events.Single(e => e.Name == "Three Day Conference");
        Assert.Equal(new DateOnly(2026, 12, 1), three.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 3), three.EndDate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>Not a calendar at all</body></html>")]
    [InlineData("BEGIN:VCALENDAR\r\nEND:VCALENDAR")]
    public void ContentThatIsNotACalendarYieldsNothingRatherThanThrowing(string content)
    {
        // VICA, VRCA and UDI answer ?ical=1 with their HTML page. A parser that
        // throws on that takes the ingest run down with it.
        Assert.Empty(Parser().Parse(content, Source, Today));
    }

    [Fact]
    public void AnEventWithNoSummaryIsSkippedRatherThanNamedEmpty()
    {
        const string NoName = """
BEGIN:VCALENDAR
BEGIN:VEVENT
DTSTART;VALUE=DATE:20261110
URL:https://example.test/x
END:VEVENT
END:VCALENDAR
""";
        Assert.Empty(Parser().Parse(NoName, Source, Today));
    }

    [Fact]
    public void ARepeatedPropertyKeepsTheFirstValue()
    {
        // Events routinely carry several ATTACH lines; a later one must not
        // overwrite a property already read.
        const string Repeated = """
BEGIN:VCALENDAR
BEGIN:VEVENT
DTSTART;VALUE=DATE:20261110
SUMMARY:Real Name
SUMMARY:Overwritten Name
END:VEVENT
END:VCALENDAR
""";
        Assert.Equal("Real Name", Assert.Single(Parser().Parse(Repeated, Source, Today)).Name);
    }
}
