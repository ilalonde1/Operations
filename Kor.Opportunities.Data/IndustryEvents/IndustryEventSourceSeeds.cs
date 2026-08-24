#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.IndustryEvents;

/// <summary>
/// The association calendars the BD event list is supposed to cover.
///
/// Single definition, used by the worker's bootstrap hosted service. Rows are
/// written to <c>opportunities.IndustryEventSource</c> with a guarded INSERT,
/// so an operator's edits in the database always win over what is listed here.
///
/// Entries with <see cref="Unmapped"/> are seeded inactive on purpose: those
/// associations publish bespoke HTML with no iCal or schema.org markup
/// (verified 2026-08-24), so each needs its own parser before it can go live.
/// They are recorded so the coverage gap is queryable:
///     SELECT Name FROM opportunities.IndustryEventSource WHERE ParserKey = 'unmapped';
/// </summary>
public static class IndustryEventSourceSeeds
{
    public const string Unmapped = "unmapped";

    public static IReadOnlyList<IndustryEventSourceSeed> Default { get; } =
    [
        // ICBA — the gap that prompted this ingest. Ten events a year including
        // four regional "Meet the Generals & Owners", the largest GC/owner
        // networking events in BC and AB, none of which were tracked.
        new IndustryEventSourceSeed(
            Name: "ICBA",
            Organizer: "Independent Contractors and Businesses Association (ICBA)",
            CalendarUrl: "https://icba.ca/events",
            ParserKey: IcbaCardCalendarParser.Key,
            SiteUrl: "https://icba.ca/",
            Region: "CA-BC",
            DefaultMarket: "British Columbia / Alberta",
            DefaultEventType: "networking",
            KorRelevance:
                "Highest GC/owner density of any western-Canada series. ICBA is named in eight "
                + "IntelAction recommendations as the venue to reach KOR targets (Scott Construction, "
                + "VanMar and others). Meet the Generals runs Vancouver, Victoria, Kelowna, Calgary "
                + "and Edmonton — all five KOR markets.",
            CrawlDelaySeconds: 86400),

        new IndustryEventSourceSeed(
            Name: "VRCA",
            Organizer: "Vancouver Regional Construction Association (VRCA)",
            CalendarUrl: "https://www.vrca.ca/events/",
            ParserKey: Unmapped,
            SiteUrl: "https://www.vrca.ca/",
            Region: "CA-BC",
            DefaultMarket: "Lower Mainland",
            DefaultEventType: "association",
            IsActive: false),

        new IndustryEventSourceSeed(
            Name: "VICA",
            Organizer: "Vancouver Island Construction Association (VICA)",
            CalendarUrl: "https://www.vica.bc.ca/events/",
            ParserKey: Unmapped,
            SiteUrl: "https://www.vica.bc.ca/",
            Region: "CA-BC",
            DefaultMarket: "Vancouver Island",
            DefaultEventType: "association",
            IsActive: false),

        new IndustryEventSourceSeed(
            Name: "UDI BC",
            Organizer: "Urban Development Institute (UDI BC)",
            CalendarUrl: "https://www.udi.bc.ca/events/",
            ParserKey: Unmapped,
            SiteUrl: "https://www.udi.bc.ca/",
            Region: "CA-BC",
            DefaultMarket: "British Columbia",
            DefaultEventType: "networking",
            IsActive: false),

        new IndustryEventSourceSeed(
            Name: "SEABC",
            Organizer: "Structural Engineers Association of British Columbia (SEABC)",
            CalendarUrl: "https://seabc.ca/events/",
            ParserKey: Unmapped,
            SiteUrl: "https://seabc.ca/",
            Region: "CA-BC",
            DefaultMarket: "British Columbia",
            DefaultEventType: "association",
            IsActive: false),

        new IndustryEventSourceSeed(
            Name: "ACEC-BC",
            Organizer: "Association of Consulting Engineering Companies BC (ACEC-BC)",
            CalendarUrl: "https://www.acec-bc.ca/events/",
            ParserKey: Unmapped,
            SiteUrl: "https://www.acec-bc.ca/",
            Region: "CA-BC",
            DefaultMarket: "British Columbia",
            DefaultEventType: "association",
            IsActive: false),
    ];
}
