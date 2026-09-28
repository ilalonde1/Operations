-- Migration 329 (2026-09-28): remove the duplicate VICA source, and make a
-- second one impossible.
--
-- ⚠⚠ THIS IS A FAULT I CAUSED AND THEN FOUND. Migration 324 corrected VICA's
--    CalendarUrl on 2026-09-23 at 13:27 — vica.bc.ca does not resolve at all,
--    the association is at vicabc.ca. The seeder in
--    SqlIndustryEventSourceStore.EnsureAsync matched existing rows on
--    CalendarUrl, so at the next Worker start it looked for the OLD url, did
--    not find it, and INSERTED A SECOND VICA at 14:00:27. No error. Two rows.
--
--    The same thing was queued up for ACEC-BC, whose feed url moved to
--    ?ical=1 in migration 328 — the next deploy would have minted a third
--    event source.
--
-- THE CLASS: a seeded table whose upsert keys on a MUTABLE field cannot tell a
-- corrected value from a new record. Every data fix to that field silently
-- becomes a duplicate on the next start. EnsureAsync now keys on Name, which is
-- the source's identity; the url is a fact about it that is expected to change.
--
-- This migration clears the duplicate and adds the unique index that makes the
-- fault structurally impossible rather than merely fixed.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

SELECT 'before' AS Section, Id, Name, CalendarUrl, IsActive,
       (SELECT COUNT(*) FROM opportunities.IndustryEvents e WHERE e.IndustryEventSourceId = s.Id) AS Events
FROM opportunities.IndustryEventSource s WHERE s.Name = N'VICA' ORDER BY s.Id;

-- Keep the OLDEST row per name — it is the one the seeder created first and the
-- one any history points at. Move any events off the duplicates before removing
-- them; VICA has none, but the tool must not depend on that.
IF OBJECT_ID('tempdb..#keep') IS NOT NULL DROP TABLE #keep;
SELECT Name, MIN(Id) AS KeepId
INTO #keep
FROM opportunities.IndustryEventSource
GROUP BY Name HAVING COUNT(*) > 1;

UPDATE e
SET e.IndustryEventSourceId = k.KeepId
FROM opportunities.IndustryEvents e
JOIN opportunities.IndustryEventSource s ON s.Id = e.IndustryEventSourceId
JOIN #keep k ON k.Name = s.Name
WHERE e.IndustryEventSourceId <> k.KeepId;

DELETE s
FROM opportunities.IndustryEventSource s
JOIN #keep k ON k.Name = s.Name
WHERE s.Id <> k.KeepId;

SELECT 'duplicates removed' AS Section, @@ROWCOUNT AS Rows;

-- The VICA row that survives must carry the CORRECT url, not the dead one.
UPDATE opportunities.IndustryEventSource
SET CalendarUrl = N'https://www.vicabc.ca/events/',
    SiteUrl     = N'https://www.vicabc.ca/',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Name = N'VICA';

-- Structural, so this cannot recur even if a future seeder regresses.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_IndustryEventSource_Name'
                 AND object_id = OBJECT_ID('opportunities.IndustryEventSource'))
BEGIN
    CREATE UNIQUE INDEX UX_IndustryEventSource_Name
        ON opportunities.IndustryEventSource ([Name]);
END
GO

SELECT 'after' AS Section, Id, Name, ParserKey, IsActive, CalendarUrl
FROM opportunities.IndustryEventSource ORDER BY IsActive DESC, Name;

SELECT 'names now unique' AS Section,
       COUNT(*) AS Sources, COUNT(DISTINCT Name) AS DistinctNames
FROM opportunities.IndustryEventSource;
GO
