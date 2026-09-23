-- Migration 324 (2026-09-23): Building Defence 2026 (DND/DCC Industry Days),
-- Defence Construction Canada as an event source, and two repairs to the
-- sources that were already there.
--
-- Prompted by Jim DesRoches's email of 2026-09-23. Checking what he sent
-- against the system: the DCC procurement channel he recommends is ALREADY
-- covered — MERX-DCC is live, 94 opportunities, ingested that morning — and the
-- awarded-contracts PDF he links is a worse version of the 20,400 DCC/DND award
-- rows we already hold. The event itself was the one real gap: IndustryEvents
-- held 93 rows and none matched defence, DCC or industry days.
--
-- Event details are from the live page, not from the email:
--   https://www.dcc-cdc.gc.ca/industry-days-2026
--   9 and 10 November 2026, Rogers Centre Ottawa or virtual, 7:30–17:00.
--   In person $755 early / $815; VIRTUAL $400 — the email's figure is right.
--   ⚠ It is a TWO-day event. The email says three.
--   Last year's inaugural event drew 800+ participants.
--
-- SourceKey is SHA1('<name>|<yyyy-MM-dd>') lower-hex, matching
-- IndustryEventIngestService.BuildSourceKey, so that if a parser is ever written
-- for this source the ingested row MERGES onto this hand-curated one instead of
-- duplicating it. The name deliberately carries no '|' — that character is the
-- key's own separator.
--   ⚠ HASHBYTES over VARCHAR, never NVARCHAR: UTF-16 bytes hash differently and
--     the ingested twin would not match.
--
-- TWO REPAIRS TO WHAT WAS ALREADY THERE
--
--  1. VICA's CalendarUrl pointed at https://www.vica.bc.ca/events/, and THAT
--     DOMAIN DOES NOT RESOLVE — curl returns 000, not a 404. The association is
--     at vicabc.ca. Had a parser ever been written, this source would have
--     failed forever for a reason nobody would have looked for.
--  2. SEABC returns 403 to a normal browser user-agent with full Accept
--     headers — bot protection, not a broken link. Recorded so the next person
--     does not spend the afternoon rediscovering it.
--
-- ⚠ NOT FIXED, because it is a build and not a data change: five of the six
--    event sources carry ParserKey 'unmapped' and IsActive = 0, and only
--    'icba-cards' exists in code. ACEC-BC, UDI BC and VRCA all serve their
--    calendars to a plain GET right now (200, 392 KB / 115 KB / 185 KB), so
--    three parsers would light three of them up. That is the real gap in this
--    area and it is bigger than the missing event was.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

DECLARE @now datetimeoffset = sysdatetimeoffset();

-------------------------------------------------------------------------------
-- 1. Defence Construction Canada as an event source.
-------------------------------------------------------------------------------
DECLARE @dcc bigint;
SELECT @dcc = Id FROM opportunities.IndustryEventSource WHERE Name = N'DCC';

IF @dcc IS NULL
BEGIN
    INSERT INTO opportunities.IndustryEventSource
        (Name, Organizer, CalendarUrl, SiteUrl, ParserKey, Region, DefaultMarket,
         DefaultEventType, KorRelevance, IsActive, CrawlDelaySeconds, CreatedAtUtc, UpdatedAtUtc)
    VALUES
        (N'DCC',
         N'Defence Construction Canada (DCC) / Department of National Defence (DND)',
         N'https://www.dcc-cdc.gc.ca/industry-days-2026',
         N'https://www.dcc-cdc.gc.ca/',
         N'unmapped',
         N'CA-NATIONAL',
         N'Canada',
         N'conference',
         N'The federal defence infrastructure programme, and the only channel on this list that is not a private developer market. DCC tenders already arrive through the MERX-DCC source; this is the room where the people who plan and procure them are.',
         0, 86400, @now, @now);
    SET @dcc = SCOPE_IDENTITY();
END
ELSE
BEGIN
    UPDATE opportunities.IndustryEventSource
    SET Organizer = N'Defence Construction Canada (DCC) / Department of National Defence (DND)',
        CalendarUrl = N'https://www.dcc-cdc.gc.ca/industry-days-2026',
        SiteUrl = N'https://www.dcc-cdc.gc.ca/',
        Region = N'CA-NATIONAL', DefaultMarket = N'Canada',
        DefaultEventType = N'conference', UpdatedAtUtc = @now
    WHERE Id = @dcc;
END;

-------------------------------------------------------------------------------
-- 2. Building Defence 2026.
-------------------------------------------------------------------------------
DECLARE @name nvarchar(300) = N'Building Defence 2026 (DND/DCC Industry Days)';
DECLARE @start date = '2026-11-09';
DECLARE @key nvarchar(64) =
    LOWER(CONVERT(CHAR(40), HASHBYTES('SHA1',
        CAST(@name + '|' + CONVERT(varchar(10), @start, 23) AS VARCHAR(8000))), 2));

MERGE opportunities.IndustryEvents AS t
USING (SELECT @key AS SourceKey) AS s
ON t.SourceKey = s.SourceKey
WHEN MATCHED THEN UPDATE SET
    t.StartDate = @start, t.EndDate = '2026-11-10',
    t.RetiredAtUtc = NULL, t.RetiredReason = NULL,
    t.IndustryEventSourceId = @dcc, t.LastSeenAtUtc = @now, t.UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
    (Name, Organizer, EventType, StartDate, EndDate, Recurrence, City, Market,
     Format, SectorsThemes, Audience, TargetsPresent, RegistrationUrl, CostNote,
     KorRelevance, SourceNote, SourceKey, IndustryEventSourceId,
     LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc)
    VALUES
    (@name,
     N'Defence Construction Canada (DCC) and the Department of National Defence (DND)',
     N'conference',
     @start, '2026-11-10',
     N'annual',
     N'Ottawa',
     N'Canada',
     N'Rogers Centre, Ottawa — or attend virtually. 7:30 a.m. to 5:00 p.m., both official languages.',
     N'Defence infrastructure; federal procurement; DND capital programme',
     N'Industry, DND and DCC. Closed to media; registered participants only.',
     N'DND and DCC decision-makers who plan, procure and deliver the federal defence infrastructure programme. 800+ attended the inaugural 2025 event.',
     N'https://www.dcc-cdc.gc.ca/industry-days-2026',
     N'In person $755 early bird / $815. VIRTUAL ATTENDANCE $400, which covers all sessions across both days.',
     N'The procurement process for defence work, from the people who run it. We already receive DCC tenders through MERX-DCC and hold 20,400 DCC/DND award records — what we do not have is the relationships. Virtual at $400 for one registration, shown in the boardroom, is the cheapest way to test whether this channel is worth pursuing.',
     N'Verified against https://www.dcc-cdc.gc.ca/industry-days-2026 on 2026-09-23. Raised by Jim DesRoches, on the advice of a DCC/DND consultant who has worked this channel since 1995.',
     @key, @dcc, @now, @now, @now);

-------------------------------------------------------------------------------
-- 3. Repairs.
-------------------------------------------------------------------------------
UPDATE opportunities.IndustryEventSource
SET CalendarUrl = N'https://www.vicabc.ca/events/',
    SiteUrl = N'https://www.vicabc.ca/',
    LastErrorMessage = N'CalendarUrl was https://www.vica.bc.ca/events/ until 2026-09-23 — that domain does not resolve (curl 000). Corrected to vicabc.ca.',
    UpdatedAtUtc = @now
WHERE Name = N'VICA' AND CalendarUrl <> N'https://www.vicabc.ca/events/';

UPDATE opportunities.IndustryEventSource
SET LastErrorMessage = N'Returns HTTP 403 to a browser user-agent with full Accept headers (checked 2026-09-23) — bot protection, not a dead link. A parser for this source needs a session or a different route in.',
    UpdatedAtUtc = @now
WHERE Name = N'SEABC';

-------------------------------------------------------------------------------
SELECT 'the event, as now held' AS Section;
SELECT e.Name, e.StartDate, e.EndDate, e.City, e.CostNote, s.Name AS Src
FROM opportunities.IndustryEvents e
LEFT JOIN opportunities.IndustryEventSource s ON s.Id = e.IndustryEventSourceId
WHERE e.SourceKey = @key;

SELECT 'event sources after this migration' AS Section;
SELECT Name, ParserKey, IsActive, CalendarUrl, LEFT(ISNULL(LastErrorMessage,''), 60) AS Note
FROM opportunities.IndustryEventSource ORDER BY Name;
GO
