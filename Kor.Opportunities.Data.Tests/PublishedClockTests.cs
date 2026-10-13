using System;
using System.Collections.Generic;
using System.IO;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Data.Ingestion.Providers;
using Kor.Opportunities.Data.Ingestion.Scraping;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

/// <summary>
/// The differential for one class of defect: <b>a source's published local wall
/// clock stored as though it were UTC.</b>
/// </summary>
/// <remarks>
/// <para>
/// Measured on production 2026-10-13: 2,010 of 2,377 deadline-bearing
/// opportunities were early by their source's UTC offset — 1,757 by 7 hours
/// (Pacific daylight), 63 by 8 (Pacific standard), 105 by 6 (Mountain), 85 by
/// 4–5 (Eastern). Every parse site passed
/// <c>DateTimeStyles.AssumeUniversal</c>, which is right only for a string that
/// carries a numeric offset or is genuinely UTC.
/// </para>
/// <para><b>What these tests compare:</b> for each affected parser, the instant
/// returned for a timestamp string taken from production data or from the
/// portal's own rendering, against the instant a bidder reading that portal
/// would act on. They cover both sides of a DST boundary per zone, the
/// abbreviation-less portal case (BC Bid), the offset-bearing case that must
/// stay untouched (SamGov), the proven-UTC feed that must stay untouched
/// (LA City RAMP), and rejection of ambiguous/non-existent local times.
/// </para>
/// <para><b>What they do NOT compare, and a same-class fault they would miss:</b>
/// they do not assert which DOM cell or JSON field each scraper reads, so a
/// portal that moves its closing date into a different column — or starts
/// rendering a zone abbreviation this map does not hold — still yields a wrong
/// or null deadline with these green. They also do not cover
/// <c>PostedDateUtc</c> for every source, and BC Bid's exact cell text is not
/// fixture-captured: the strings below are shapes
/// <c>DateTimeOffset.TryParse</c> accepts, and the portal's real rendering is
/// evidenced only by 1,678 rows having parsed successfully in production. A
/// standing check on stored data is the second gate —
/// <c>DataHealthAuditJob</c>'s wall-clock audit — because a differential cannot
/// see a fault present in both runs.
/// </para>
/// </remarks>
public sealed class PublishedClockTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi)
        => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- bids&tenders
    // Real RawJson closing cells. Leduc's is the row that exposed the class:
    // 2026-RFQ-CPS-064 closed "Wed Oct 21, 2026 2:00:00 PM (MDT)" and was stored
    // as 14:00Z — six hours early.
    [Theory]
    [InlineData("Wed Oct 21, 2026 2:00:00 PM (MDT)", 2026, 10, 21, 20, 0)] // Leduc, Mountain daylight
    [InlineData("Thu May 21, 2026 3:00 PM (PDT)", 2026, 5, 21, 22, 0)]     // Pacific daylight
    [InlineData("Fri Nov 20, 2026 3:00 PM (PST)", 2026, 11, 20, 23, 0)]    // Pacific standard
    [InlineData("Tue Jan 13, 2026 2:00:00 PM (MST)", 2026, 1, 13, 21, 0)]  // Mountain standard
    public void BidsAndTenders_closing_cell_converts_from_its_named_zone(
        string cell, int y, int mo, int d, int h, int mi)
        => Assert.Equal(Utc(y, mo, d, h, mi), BidsAndTendersScraper.ParseClosingDate(cell));

    [Theory]
    [InlineData("Wed Oct 21, 2026 2:00:00 PM (MDT)", 2026, 10, 21, 20, 0)]
    [InlineData("Thu May 21, 2026 3:00 PM (PDT)", 2026, 5, 21, 22, 0)]
    public void BidsAndTendersAwards_closing_cell_converts_from_its_named_zone(
        string cell, int y, int mo, int d, int h, int mi)
        => Assert.Equal(Utc(y, mo, d, h, mi), BidsAndTendersAwardsScraper.ParseClosingDate(cell));

    // ---------------------------------------------------------------------- BC Bid
    // The grid column is headed "closing date and time (pacific time)" and the
    // cell text carries no abbreviation, so the zone is a property of the portal.
    [Theory]
    [InlineData("2026-10-27 2:00 PM", 2026, 10, 27, 21, 0)]  // PDT
    [InlineData("2026-12-15 2:00 PM", 2026, 12, 15, 22, 0)]  // PST
    public void BcBid_grid_cell_is_pacific_not_utc(string cell, int y, int mo, int d, int h, int mi)
        => Assert.Equal(Utc(y, mo, d, h, mi), BcBidScraper.ParseBcBidDate(cell));

    [Theory]
    [InlineData("May 21, 2026 3:00 PM (PDT)", 2026, 5, 21, 22, 0)]
    [InlineData("2026-10-27 2:00 PM", 2026, 10, 27, 21, 0)]
    public void BcBidHistorical_is_pacific_not_utc(string cell, int y, int mo, int d, int h, int mi)
        => Assert.Equal(Utc(y, mo, d, h, mi), BcBidHistoricalScraper.ParseDate(cell));

    [Theory]
    [InlineData("May 21, 2026 3:00 PM (PDT)", 2026, 5, 21, 22, 0)]
    [InlineData("2026-10-27 2:00 PM", 2026, 10, 27, 21, 0)]
    public void BcBidUnverifiedBidResults_is_pacific_not_utc(
        string cell, int y, int mo, int d, int h, int mi)
        => Assert.Equal(Utc(y, mo, d, h, mi), BcBidUnverifiedBidResultsScraper.ParseDate(cell));

    // ------------------------------------------------------------------ Bonfire RSS
    // Real description prose: "Project closes Nov 02, 2026 2:00 PM PST."
    [Theory]
    [InlineData("Description: AHU renewal. Project closes Nov 02, 2026 2:00 PM PST.",
        2026, 11, 2, 22, 0)]
    [InlineData("Closes Jun 10, 2026 2:00 PM PDT.", 2026, 6, 10, 21, 0)]
    public void Bonfire_rss_prose_converts_from_its_named_zone(
        string description, int y, int mo, int d, int h, int mi)
        => Assert.Equal(Utc(y, mo, d, h, mi),
            RssOpportunityProvider.ParseDeadlineFromDescription(description));

    // ------------------------------------------------------- the must-not-break set
    // SamGov publishes a numeric offset; it was already correct (16 of 18 rows)
    // and must stay correct.
    [Fact]
    public void A_string_carrying_its_own_offset_is_honoured_not_reinterpreted()
        => Assert.Equal(Utc(2025, 5, 1, 19, 0),
            PublishedClock.Parse("2025-05-01T12:00:00-07:00", "America/Vancouver"));

    // LA City RAMP's closedate is already UTC: 28 of 28 rows carrying a local
    // clock in their own title matched it at +0h. A feed proven to publish UTC
    // passes a null zone and must be left alone.
    [Fact]
    public void A_proven_utc_feed_is_left_alone()
        => Assert.Equal(Utc(2026, 10, 16, 22, 0),
            PublishedClock.Parse("2026-10-16T22:00:00.000", fallbackZoneId: null));

    // CanadaBuys publishes a DST-aware Eastern wall clock: the same field value
    // renders "14:00 EDT" on 2026-10-28 and "14:00 EST" on 2026-02-18, which is
    // NOT the fixed UTC-0500 its dataset documentation claims.
    [Theory]
    [InlineData("2026-10-28T14:00:00", 2026, 10, 28, 18, 0)]
    [InlineData("2026-02-18T14:00:00", 2026, 2, 18, 19, 0)]
    public void CanadaBuys_naked_iso_is_eastern_wall_clock(
        string value, int y, int mo, int d, int h, int mi)
        => Assert.Equal(Utc(y, mo, d, h, mi), PublishedClock.Parse(value, "America/Toronto"));

    // ------------------------------------------------------------------ DST edges
    // 2026-03-08 02:30 does not exist in Pacific; 2026-11-01 01:30 happens twice.
    // Both are rejected rather than silently resolved to one instant.
    [Theory]
    [InlineData("2026-03-08 02:30")]
    [InlineData("2026-11-01 01:30")]
    public void An_impossible_or_ambiguous_local_time_is_rejected_not_guessed(string cell)
        => Assert.Null(PublishedClock.Parse(cell, "America/Vancouver"));

    [Fact]
    public void A_zone_abbreviation_outside_the_map_yields_no_timestamp_rather_than_a_wrong_one()
        => Assert.False(PublishedClock.TryFromNamedZone("Wed Oct 21, 2026 2:00 PM (XYZ)", out _));

    // ------------------------------------------------- date-only closing values
    // "Posting expires: Wednesday, October 28, 2026" names no closing time. Stored
    // at midnight it reads as 17:00 on the 27th Pacific, and DataRetirementJob
    // expires on SubmissionDeadlineUtc < now -- so the whole final day is lost and
    // the opportunity vanishes before it closes. 249 rows were in that state.
    [Theory]
    [InlineData("October 28, 2026", "America/Vancouver", 2026, 10, 29, 6, 59, 59)]  // 23:59:59 PDT
    [InlineData("December 10, 2026", "America/Vancouver", 2026, 12, 11, 7, 59, 59)] // 23:59:59 PST
    [InlineData("2027/01/06", "America/Toronto", 2027, 1, 7, 4, 59, 59)]            // 23:59:59 EST
    public void A_date_only_deadline_closes_at_the_end_of_that_local_day(
        string published, string zone, int y, int mo, int d, int h, int mi, int s)
        => Assert.Equal(new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.Zero),
            PublishedClock.ParseDeadline(published, zone));

    [Fact]
    public void A_date_only_deadline_with_no_known_zone_is_left_at_midnight_utc()
        => Assert.Equal(Utc(2026, 10, 28, 0, 0),
            PublishedClock.ParseDeadline("October 28, 2026", fallbackZoneId: null));

    [Fact]
    public void A_posted_date_is_NOT_pushed_to_the_end_of_the_day()
        => Assert.Equal(Utc(2026, 10, 28, 7, 0),   // 00:00 PDT, start of day
            PublishedClock.Parse("October 28, 2026", "America/Vancouver"));

    [Theory]
    [InlineData("Posting expires: Wednesday, October 28, 2026.", 2026, 10, 29, 6, 59, 59)]
    public void Civicinfo_prose_closes_at_the_end_of_its_day_when_the_feed_zone_is_known(
        string description, int y, int mo, int d, int h, int mi, int s)
        => Assert.Equal(new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.Zero),
            RssOpportunityProvider.ParseDeadlineFromDescription(
                description, "America/Vancouver"));

    [Fact]
    public void BcBid_a_closing_cell_with_no_time_closes_at_end_of_day_pacific()
        => Assert.Equal(new DateTimeOffset(2026, 10, 29, 6, 59, 59, TimeSpan.Zero),
            BcBidScraper.ParseBcBidDeadline("2026-10-28"));

    // ------------------------------------- the published date must stay recoverable
    // PublishedClock keeps the SOURCE's offset, so .Date is the date the buyer
    // published on. IngestionService derives RfpReleaseDate from it; taking the UTC
    // date instead moves any posting made at 5pm Pacific or later to the next day,
    // and 107 BC Bid observations sit at hour 17-23 local.
    [Theory]
    [InlineData("2026-10-20 18:00", 2026, 10, 20)]  // evening: UTC date would be the 21st
    [InlineData("2026-10-20 23:30", 2026, 10, 20)]
    [InlineData("2026-10-20 09:00", 2026, 10, 20)]
    public void The_published_local_date_survives_conversion(
        string cell, int y, int mo, int d)
    {
        var parsed = BcBidScraper.ParseBcBidDate(cell);
        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(y, mo, d), parsed!.Value.Date);
    }

    [Fact]
    public void Keeping_the_offset_does_not_change_the_instant()
    {
        // Same moment, two representations: equality and comparison are by instant,
        // which is what every deadline query depends on.
        var kept = BcBidScraper.ParseBcBidDate("2026-10-20 18:00")!.Value;
        Assert.Equal(Utc(2026, 10, 21, 1, 0), kept);
        Assert.Equal(TimeSpan.FromHours(-7), kept.Offset);
    }

    // ------------------------------------------------------------ the differential
    /// <summary>
    /// Proves the expectations above are not vacuous, by running the EXACT parse
    /// every site used before this fix — strip any trailing zone, then
    /// <c>AssumeUniversal | AdjustToUniversal</c> — and asserting it returns the
    /// published WALL CLOCK rather than the instant. That wall clock is what is
    /// sitting in the database on 2,010 rows, so each case below is a row shape
    /// this fix changes. Revert any parser to the old flags and its test fails.
    /// </summary>
    [Theory]
    [InlineData("Wed Oct 21, 2026 2:00:00 PM (MDT)", 2026, 10, 21, 14, 0, 20, 0)]
    [InlineData("Thu May 21, 2026 3:00 PM (PDT)", 2026, 5, 21, 15, 0, 22, 0)]
    [InlineData("Fri Nov 20, 2026 3:00 PM (PST)", 2026, 11, 20, 15, 0, 23, 0)]
    [InlineData("Tue Jan 13, 2026 2:00:00 PM (MST)", 2026, 1, 13, 14, 0, 21, 0)]
    [InlineData("2026-10-28T14:00:00", 2026, 10, 28, 14, 0, 18, 0)]
    public void The_old_parse_stored_the_wall_clock_and_the_new_parse_stores_the_instant(
        string published, int y, int mo, int d,
        int oldHour, int oldMinute, int newHour, int newMinute)
    {
        var stripped = System.Text.RegularExpressions.Regex.Replace(
            published, @"\s*\([A-Z]{2,5}\)\s*$", string.Empty).Trim();
        Assert.True(DateTimeOffset.TryParse(
            stripped, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
            | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var oldResult));

        // What the database holds today: the wall clock relabelled as UTC.
        Assert.Equal(Utc(y, mo, d, oldHour, oldMinute), oldResult);

        // What a bidder reading the portal would act on.
        var zone = published.StartsWith("2026-10-28", StringComparison.Ordinal)
            ? "America/Toronto"
            : null;
        var corrected = zone is null
            ? PublishedClock.Parse(published, "America/Vancouver")
            : PublishedClock.Parse(published, zone);
        Assert.Equal(Utc(y, mo, d, newHour, newMinute), corrected);
        Assert.NotEqual(oldResult, corrected);
    }

    // ------------------------------------------------------------------ the gate
    /// <summary>
    /// No ingestion source file may use <c>DateTimeStyles.AssumeUniversal</c>.
    /// <see cref="PublishedClock"/> is the only place allowed to decide what an
    /// offset-less timestamp means.
    /// </summary>
    /// <remarks>
    /// This is the check that would have prevented the whole class. The flag was
    /// copied into 15 parse sites across providers and scrapers; each looked
    /// locally reasonable, and every one that read a local wall clock stored it
    /// early by the source's offset — 2,116 of 2,473 deadlines. A comment saying
    /// "don't use this" would not have held: this repo's own history is that
    /// prose loses to momentum and only mechanical checks stick.
    /// <para>
    /// WHAT IT COVERS: the literal token in .cs files under the two ingestion
    /// folders. WHAT IT DOES NOT: the same mistake written a different way —
    /// <c>DateTime.Parse</c> on a naked string followed by
    /// <c>.ToUniversalTime()</c>, or <c>new DateTimeOffset(local, TimeSpan.Zero)</c>,
    /// both of which relabel a wall clock just as silently. It also says nothing
    /// about whether a given source's configured zone is the RIGHT zone; that is
    /// evidence, not syntax, and lives in the per-source cases above.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_ingestion_file_may_assume_an_offsetless_timestamp_is_utc()
    {
        var root = FindRepoRoot();
        var folders = new[]
        {
            Path.Combine(root, "Kor.Opportunities.Data", "Ingestion"),
            Path.Combine(root, "Kor.Opportunities.Core", "Ingestion"),
        };

        var offenders = new List<string>();
        foreach (var folder in folders)
        {
            Assert.True(Directory.Exists(folder), $"expected ingestion folder at {folder}");
            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || Path.GetFileName(file) == "PublishedClock.cs")
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (!line.Contains("DateTimeStyles.AssumeUniversal", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // A comment explaining the defect is allowed; using it is not.
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "DateTimeStyles.AssumeUniversal relabels a source's local wall clock as UTC. "
            + "Use PublishedClock.Parse / ParseDeadline with the zone the SOURCE publishes in, "
            + "or null only for a feed proven to publish UTC. Offenders: "
            + string.Join(", ", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Opportunities.Core")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void Every_abbreviation_the_portals_render_resolves_to_a_zone()
    {
        foreach (var abbr in new[] { "PST", "PDT", "PT", "MST", "MDT", "MT",
                                     "CST", "CDT", "CT", "EST", "EDT", "ET",
                                     "AST", "ADT", "NST", "NDT" })
        {
            var zoneId = PublishedClock.ZoneIdForAbbreviation(abbr);
            Assert.NotNull(zoneId);
            Assert.NotNull(PublishedClock.ResolveZone(zoneId));
        }
    }
}
