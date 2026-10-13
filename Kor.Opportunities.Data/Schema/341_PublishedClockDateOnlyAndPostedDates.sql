-- 341: the two remaining faults in the same class as 339/340.
--
-- (A) A DATE-ONLY DEADLINE EXPIRES BEFORE THE DATE IT PRINTS.
--     "Posting expires: Wednesday, October 28, 2026" names no closing time and
--     was stored as 2026-10-28T00:00:00Z -- which is 17:00 on the 27th in
--     Pacific. DataRetirementJob expires on
--     SubmissionDeadlineUtc < SYSDATETIMEOFFSET(), so the whole final day is
--     lost and the opportunity disappears before it closes. 249 rows were in
--     that state, 78 of them still in the future.
--     Corrected to the LAST SECOND of the published day in the source's zone.
--     That invents no closing hour the publisher did not give and errs toward
--     keeping an opportunity visible, which is the right direction for a radar.
--
-- (B) OBSERVATION PostedDateUtc HOLDS BC BID'S PACIFIC WALL CLOCK.
--     Same AssumeUniversal fault as the deadline. The column is read for list
--     ordering and, historically, to derive Opportunity.RfpReleaseDate.
--
-- ⛔ WHY THE CORRECTED VALUES KEEP A NON-ZERO OFFSET.
--     These are written as "<wall clock> -07:00", not "<instant> +00:00". Both
--     are the same instant and datetimeoffset compares by instant, so every
--     query behaves identically -- but keeping the offset leaves the PUBLISHED
--     LOCAL DATE recoverable via .Date, and IngestionService derives
--     RfpReleaseDate from exactly that. Normalising to UTC would move any
--     posting made at 5pm Pacific or later to the following day; 107 BC Bid
--     observations sit at hour 17-23 local, so this is not hypothetical.
--     (Migration 340 normalised deadlines to +00:00. That is harmless there --
--     nothing derives a date from a deadline -- and is left alone.)
--
-- Idempotency keys on the VALUE, never on "has this row been touched": a row
-- holding a value this migration produced is skipped, and a row since
-- re-ingested as a wall clock is corrected again. 340 learned that the hard way.
--
-- HOW TO RUN
--   report only:  sqlcmd ... -i 341_...sql -v Apply=0
--   apply:        sqlcmd ... -i 341_...sql -v Apply=1
-- Apply is intentionally not defaulted; sqlcmd refuses an undefined variable,
-- and a :setvar default would override -v and silently ignore the operator.

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @DryRun bit = CASE WHEN $(Apply) = 1 THEN 0 ELSE 1 END;

-- ------------------------------------------------------------------ audit table
IF OBJECT_ID('opportunities.PublishedClockBackfill') IS NULL
BEGIN
    CREATE TABLE opportunities.PublishedClockBackfill (
        Id            bigint IDENTITY(1,1) PRIMARY KEY,
        TargetTable   nvarchar(100)  NOT NULL,
        TargetId      bigint         NOT NULL,
        ColumnName    nvarchar(60)   NOT NULL,
        SourceName    nvarchar(200)  NULL,
        ZoneId        nvarchar(64)   NOT NULL,
        Reason        nvarchar(60)   NOT NULL,
        OldValue      datetimeoffset NOT NULL,
        NewValue      datetimeoffset NOT NULL,
        AppliedAtUtc  datetimeoffset NOT NULL DEFAULT sysdatetimeoffset(),
        MigrationName nvarchar(100)  NOT NULL
    );
    CREATE INDEX IX_PublishedClockBackfill_Target
        ON opportunities.PublishedClockBackfill(TargetTable, TargetId, ColumnName);

    EXEC sys.sp_addextendedproperty
        @name = N'Rollback',
        @value = N'Deadlines: UPDATE o SET o.SubmissionDeadlineUtc = b.OldValue FROM opportunities.Opportunities o JOIN opportunities.PublishedClockBackfill b ON b.TargetId = o.Id AND b.TargetTable = N''Opportunities'' AND b.ColumnName = N''SubmissionDeadlineUtc'' AND o.SubmissionDeadlineUtc = b.NewValue; Posted: UPDATE ob SET ob.PostedDateUtc = b.OldValue FROM opportunities.OpportunityObservations ob JOIN opportunities.PublishedClockBackfill b ON b.TargetId = ob.Id AND b.TargetTable = N''OpportunityObservations'' AND b.ColumnName = N''PostedDateUtc'' AND ob.PostedDateUtc = b.NewValue;',
        @level0type = N'SCHEMA', @level0name = N'opportunities',
        @level1type = N'TABLE',  @level1name = N'PublishedClockBackfill';
END;

-- ----------------------------------------------- the verified source zone map
-- Derived from the zone abbreviations each source's own rows carry (word-bounded)
-- and cross-checked against the geography the source name states. Identical to
-- the map 340 used; CivicInfo BC is added here because its feed states BC and
-- its bids are BC municipal, and it is only now that its date-only deadlines
-- are being interpreted at all.
DECLARE @SourceZone TABLE (SourceName nvarchar(200) PRIMARY KEY, ZoneId nvarchar(64));
INSERT INTO @SourceZone (SourceName, ZoneId) VALUES
    (N'BcBid',                      N'Pacific Standard Time'),
    (N'BcBid_Engineering',          N'Pacific Standard Time'),
    (N'CivicInfoBC_All',            N'Pacific Standard Time'),
    (N'CivicInfoBC_Construction',   N'Pacific Standard Time'),
    (N'Bonfire_BCIT',               N'Pacific Standard Time'),
    (N'Bonfire_FNHA',               N'Pacific Standard Time'),
    (N'Bonfire_FraserHealth',       N'Pacific Standard Time'),
    (N'Bonfire_IslandHealth',       N'Pacific Standard Time'),
    (N'Bonfire_Kamloops',           N'Pacific Standard Time'),
    (N'Bonfire_Mission',            N'Pacific Standard Time'),
    (N'Bonfire_PHSA',               N'Pacific Standard Time'),
    (N'Bonfire_UBC',                N'Pacific Standard Time'),
    (N'Bonfire_UVic',               N'Pacific Standard Time'),
    (N'Bonfire_CbeAb',              N'Mountain Standard Time'),
    (N'APC_AllBuyers',              N'Mountain Standard Time'),
    (N'MERX-DCC',                   N'Eastern Standard Time'),  -- federal buyer (DCC)
    (N'BidsTenders_Abbotsford',     N'Pacific Standard Time'),
    (N'BidsTenders_DNV',            N'Pacific Standard Time');

-- ⛔ NOT in this map, deliberately:
--   LACity_RAMP_OpenBids -- closedate is genuinely UTC (28 of 28 cross-checked).
--                           Its 8 midnight values are a real UTC midnight, and
--                           an end-of-day shift would invent a Pacific day.
--   SamGov               -- carries its own numeric offset.

-- --------------------------------------------- rss.timeZone for the RSS feeds
-- The code only moves a date-only deadline once the feed's zone is KNOWN, so
-- these rows are what makes the fix take effect going forward.
MERGE opportunities.OpportunitySourceMappings AS target
USING (
    SELECT s.Id AS OpportunitySourceId, N'rss.timeZone' AS [Key],
           CASE z.ZoneId WHEN N'Pacific Standard Time'  THEN N'America/Vancouver'
                         WHEN N'Mountain Standard Time' THEN N'America/Edmonton'
                         WHEN N'Eastern Standard Time'  THEN N'America/Toronto' END AS ValueJson
    FROM opportunities.OpportunitySources s
    JOIN @SourceZone z ON z.SourceName = s.Name
    WHERE s.Name LIKE N'Bonfire_%' OR s.Name LIKE N'CivicInfoBC_%'
) AS src
    ON target.OpportunitySourceId = src.OpportunitySourceId AND target.[Key] = src.[Key]
WHEN MATCHED AND ISNULL(target.ValueJson, N'') <> src.ValueJson THEN
    UPDATE SET ValueJson = src.ValueJson, UpdatedAtUtc = sysdatetimeoffset()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (src.OpportunitySourceId, src.[Key], src.ValueJson, sysdatetimeoffset());

SELECT 'rss.timeZone rows' AS Report, COUNT(*) AS Rows_
FROM opportunities.OpportunitySourceMappings WHERE [Key] = N'rss.timeZone';

-- ================================================================== (A) deadlines
IF OBJECT_ID('tempdb..#dl') IS NOT NULL DROP TABLE #dl;

WITH src AS (
    SELECT ob.OpportunityId,
           MIN(z.ZoneId) AS ZoneId,
           COUNT(DISTINCT z.ZoneId) AS ZoneCount,
           COUNT(DISTINCT CASE WHEN z.ZoneId IS NULL THEN s.Name END) AS UnmappedSources,
           MIN(s.Name) AS AnySourceName
    FROM opportunities.OpportunityObservations ob
    JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
    LEFT JOIN @SourceZone z ON z.SourceName = s.Name
    GROUP BY ob.OpportunityId
)
SELECT o.Id AS OpportunityId,
       src.AnySourceName AS SourceName,
       src.ZoneId,
       o.SubmissionDeadlineUtc AS OldValue,
       -- last second of the published day, in the source's zone, offset KEPT
       CAST(DATEADD(second, -1,
              DATEADD(day, 1, CAST(CAST(o.SubmissionDeadlineUtc AS date) AS datetime2)))
            AT TIME ZONE src.ZoneId AS datetimeoffset) AS NewValue
INTO #dl
FROM opportunities.Opportunities o
JOIN src ON src.OpportunityId = o.Id
WHERE o.SubmissionDeadlineUtc IS NOT NULL
  -- date-only: the publisher gave no time of day
  AND CAST(o.SubmissionDeadlineUtc AS time) = '00:00:00'
  AND src.ZoneCount = 1
  AND src.UnmappedSources = 0
  AND NOT EXISTS (
        SELECT 1 FROM opportunities.PublishedClockBackfill b
        WHERE b.TargetTable = N'Opportunities' AND b.TargetId = o.Id
          AND b.ColumnName = N'SubmissionDeadlineUtc'
          AND b.NewValue = o.SubmissionDeadlineUtc);

-- A correction here must move the deadline FORWARD by just under a day.
DELETE FROM #dl
WHERE NewValue <= OldValue
   OR DATEDIFF(hour, OldValue, NewValue) NOT BETWEEN 15 AND 32;

SELECT '(A) date-only deadlines' AS Report, COUNT(*) AS Rows_,
       SUM(CASE WHEN OldValue > sysdatetimeoffset() THEN 1 ELSE 0 END) AS StillFuture
FROM #dl;
SELECT SourceName, ZoneId, COUNT(*) AS Rows_ FROM #dl GROUP BY SourceName, ZoneId ORDER BY Rows_ DESC;

-- ============================================================ (B) posted dates
IF OBJECT_ID('tempdb..#pd') IS NOT NULL DROP TABLE #pd;

SELECT ob.Id AS ObservationId,
       s.Name AS SourceName,
       z.ZoneId,
       ob.PostedDateUtc AS OldValue,
       -- same wall clock, reinterpreted in the portal's zone, offset KEPT
       CAST(CAST(ob.PostedDateUtc AS datetime2) AT TIME ZONE z.ZoneId AS datetimeoffset) AS NewValue
INTO #pd
FROM opportunities.OpportunityObservations ob
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
JOIN @SourceZone z ON z.SourceName = s.Name
WHERE ob.PostedDateUtc IS NOT NULL
  -- BC Bid is the only source whose posted date is a bare portal wall clock.
  -- Every other feed here publishes pubDate WITH an offset (RSS) or real UTC,
  -- so AssumeUniversal was inert for them and there is nothing to correct.
  AND s.Name IN (N'BcBid', N'BcBid_Engineering')
  AND CAST(ob.PostedDateUtc AS time) <> '00:00:00'
  AND DATEPART(tzoffset, ob.PostedDateUtc) = 0      -- still stamped +00:00
  AND NOT EXISTS (
        SELECT 1 FROM opportunities.PublishedClockBackfill b
        WHERE b.TargetTable = N'OpportunityObservations' AND b.TargetId = ob.Id
          AND b.ColumnName = N'PostedDateUtc'
          AND b.NewValue = ob.PostedDateUtc);

DELETE FROM #pd WHERE DATEDIFF(hour, OldValue, NewValue) NOT BETWEEN 4 AND 8;

SELECT '(B) BcBid posted dates' AS Report, COUNT(*) AS Rows_ FROM #pd;
SELECT SourceName, COUNT(*) AS Rows_ FROM #pd GROUP BY SourceName ORDER BY Rows_ DESC;

-- The point of keeping the offset: the published local DATE must not move.
IF EXISTS (SELECT 1 FROM #pd WHERE CAST(OldValue AS date) <> CAST(NewValue AS date))
BEGIN
    RAISERROR(N'341 aborted: a posted date changed its local calendar date. RfpReleaseDate derives from it.', 16, 1);
    RETURN;
END;

IF @DryRun = 1
BEGIN
    PRINT '';
    PRINT 'Apply=0 - nothing was changed.';
    SELECT TOP 10 'deadline' AS Kind, OpportunityId AS TargetId, SourceName,
           CONVERT(varchar(30), OldValue, 127) AS WasUtc,
           CONVERT(varchar(30), NewValue, 127) AS NowUtc
    FROM #dl ORDER BY OpportunityId;
    SELECT TOP 10 'posted' AS Kind, ObservationId AS TargetId, SourceName,
           CONVERT(varchar(30), OldValue, 127) AS WasUtc,
           CONVERT(varchar(30), NewValue, 127) AS NowUtc
    FROM #pd ORDER BY ObservationId;
    RETURN;
END;

-- ------------------------------------------------------------------- the cut
BEGIN TRANSACTION;

    INSERT INTO opportunities.PublishedClockBackfill
        (TargetTable, TargetId, ColumnName, SourceName, ZoneId, Reason,
         OldValue, NewValue, MigrationName)
    SELECT N'Opportunities', d.OpportunityId, N'SubmissionDeadlineUtc', d.SourceName,
           d.ZoneId, N'date-only deadline -> end of published day',
           d.OldValue, d.NewValue, N'341_PublishedClockDateOnlyAndPostedDates'
    FROM #dl d
    JOIN opportunities.Opportunities o
      ON o.Id = d.OpportunityId AND o.SubmissionDeadlineUtc = d.OldValue;
    DECLARE @dlAudited int = @@ROWCOUNT;

    UPDATE o SET o.SubmissionDeadlineUtc = d.NewValue
    FROM opportunities.Opportunities o
    JOIN #dl d ON d.OpportunityId = o.Id AND o.SubmissionDeadlineUtc = d.OldValue;
    DECLARE @dlUpdated int = @@ROWCOUNT;

    INSERT INTO opportunities.PublishedClockBackfill
        (TargetTable, TargetId, ColumnName, SourceName, ZoneId, Reason,
         OldValue, NewValue, MigrationName)
    SELECT N'OpportunityObservations', p.ObservationId, N'PostedDateUtc', p.SourceName,
           p.ZoneId, N'portal wall clock stamped UTC',
           p.OldValue, p.NewValue, N'341_PublishedClockDateOnlyAndPostedDates'
    FROM #pd p
    JOIN opportunities.OpportunityObservations ob
      ON ob.Id = p.ObservationId AND ob.PostedDateUtc = p.OldValue;
    DECLARE @pdAudited int = @@ROWCOUNT;

    UPDATE ob SET ob.PostedDateUtc = p.NewValue
    FROM opportunities.OpportunityObservations ob
    JOIN #pd p ON p.ObservationId = ob.Id AND ob.PostedDateUtc = p.OldValue;
    DECLARE @pdUpdated int = @@ROWCOUNT;

    IF @dlAudited <> @dlUpdated OR @pdAudited <> @pdUpdated
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'341 aborted: audited %d/%d but updated %d/%d. Nothing committed.',
                  16, 1, @dlAudited, @pdAudited, @dlUpdated, @pdUpdated);
        RETURN;
    END;

COMMIT TRANSACTION;

SELECT 'applied' AS Report, @dlUpdated AS DeadlinesFixed, @pdUpdated AS PostedDatesFixed;
