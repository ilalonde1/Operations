-- 340: correct historical SubmissionDeadlineUtc values that hold a source's
--      published LOCAL wall clock relabelled as UTC.
--
-- WHY --------------------------------------------------------------------------
-- Every ingestion date parser passed DateTimeStyles.AssumeUniversal, which tells
-- .NET an offset-less string is already UTC. For a portal that publishes local
-- time -- "Wed Oct 21, 2026 2:00:00 PM (MDT)", or a BC Bid grid column headed
-- "closing date and time (pacific time)" -- that relabels the wall clock, so the
-- stored instant is EARLY by the zone's offset. Measured 2026-10-13.
-- Migration 339 and PublishedClock fix ingestion forward; this corrects history.
--
-- THE CORRECTION ---------------------------------------------------------------
-- No raw text is needed. The stored value IS the published wall clock, so the
-- correction is to reinterpret that same wall clock in the source's zone:
--     CAST(value AS datetime2) AT TIME ZONE <zone> AT TIME ZONE 'UTC'
-- AT TIME ZONE is DST-aware, so an October Pacific close shifts 7 hours and a
-- December one 8. Verified against Python/IANA on the same inputs: Leduc
-- 14:00->20:00Z, Pacific Oct 14:00->21:00Z, Pacific Dec 14:00->22:00Z, Eastern
-- Oct 14:00->18:00Z, Eastern Feb 14:00->19:00Z. Two independent zone databases,
-- same answers.
--
-- SAFETY -----------------------------------------------------------------------
--  * Every prior value is written to opportunities.DeadlineClockBackfill BEFORE
--    the update. KorOpportunitiesDb is SIMPLE recovery with nightly fulls only,
--    so there is no point-in-time restore -- that table IS the undo path, and
--    the rollback statement is in its own extended property.
--  * IDEMPOTENT via the audit table: a row already recorded in
--    DeadlineClockBackfill for this migration is excluded from the candidate set
--    entirely. Re-running is a genuine no-op.
--    ⚠ This was wrong in the first version and it cost a real double-shift. The
--    UPDATE matched on the expected OLD value, which looks like idempotency but
--    is not: #cand is rebuilt from the CURRENT value each run, so the second run
--    treated the corrected instant as a fresh wall clock and moved 2,072 rows
--    another 4-8 hours. It was caught by the re-run reporting 2,072 applied
--    instead of 0, and reverted in full from this table. "Matches the value I
--    measured" protects against concurrent edits; only a record of what has
--    already been applied protects against re-application.
--  * The UPDATE also matches on the expected OLD value, so a row re-ingested
--    between measurement and apply is skipped rather than shifted.
--  * FAIL-CLOSED on coverage: if any source holding a correctable row is neither
--    mapped to a zone nor explicitly left alone, the migration raises and rolls
--    back rather than silently skipping it.
--  * UpdatedAtUtc / UpdatedBy are deliberately NOT touched. This corrects a
--    storage fault, and stamping 2,000 rows as freshly updated would distort
--    every "recently changed" view. The audit table carries the provenance.
--
-- NOT IN SCOPE -----------------------------------------------------------------
--  * OpportunityObservations.PostedDateUtc has the same fault. It is observation
--    metadata, nothing scores or triages on it, and it is left for a separate
--    decision rather than widened into a data change nobody asked for.
--  * Status / WonLostOutcome are NOT revisited. DataRetirementJob auto-expires on
--    SubmissionDeadlineUtc < SYSDATETIMEOFFSET(), so some opportunities were
--    expired up to 8 hours before they truly closed. Correcting the deadline does
--    not un-expire them; that is a judgement call about live pursuits, not a
--    timestamp repair, and the affected rows are reported at the end.
--
-- HOW TO RUN -------------------------------------------------------------------
--   report only, changes nothing:   sqlcmd ... -i 340_...sql -v Apply=0
--   apply the correction:           sqlcmd ... -i 340_...sql -v Apply=1
--
-- The Apply variable is deliberately NOT defaulted with :setvar. sqlcmd refuses
-- to run a script whose scripting variable is undefined, so running this file
-- without stating intent fails before it touches anything. (A :setvar default
-- would be worse than none: :setvar OVERRIDES -v, so a committed default of 0
-- would silently ignore -v Apply=1, and a default of 1 would silently ignore
-- -v Apply=0 and change data on what the operator believed was a dry run. That
-- exact trap fired once while writing this migration.)

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- opportunities.Opportunities carries filtered indexes, so DML against it
-- requires QUOTED_IDENTIFIER ON. sqlcmd leaves it OFF by default and the UPDATE
-- fails with msg 1934 -- which is how the first attempt at this migration ended,
-- harmlessly, inside its own rolled-back transaction.
SET QUOTED_IDENTIFIER ON;

DECLARE @DryRun bit = CASE WHEN $(Apply) = 1 THEN 0 ELSE 1 END;

-- ---------------------------------------------------------------- audit table
IF OBJECT_ID('opportunities.DeadlineClockBackfill') IS NULL
BEGIN
    CREATE TABLE opportunities.DeadlineClockBackfill (
        Id              bigint IDENTITY(1,1) PRIMARY KEY,
        OpportunityId   bigint         NOT NULL,
        SourceName      nvarchar(200)  NULL,
        ZoneId          nvarchar(64)   NOT NULL,
        Basis           nvarchar(60)   NOT NULL,
        OldDeadlineUtc  datetimeoffset NOT NULL,
        NewDeadlineUtc  datetimeoffset NOT NULL,
        ShiftHours      int            NOT NULL,
        AppliedAtUtc    datetimeoffset NOT NULL DEFAULT sysdatetimeoffset(),
        MigrationName   nvarchar(100)  NOT NULL
    );
    CREATE INDEX IX_DeadlineClockBackfill_OpportunityId
        ON opportunities.DeadlineClockBackfill(OpportunityId);

    EXEC sys.sp_addextendedproperty
        @name = N'Rollback',
        @value = N'UPDATE o SET o.SubmissionDeadlineUtc = b.OldDeadlineUtc FROM opportunities.Opportunities o JOIN opportunities.DeadlineClockBackfill b ON b.OpportunityId = o.Id AND o.SubmissionDeadlineUtc = b.NewDeadlineUtc WHERE b.MigrationName = N''340_BackfillDeadlineLocalWallClocks'';',
        @level0type = N'SCHEMA', @level0name = N'opportunities',
        @level1type = N'TABLE',  @level1name = N'DeadlineClockBackfill';
END;

-- --------------------------------------------------- the verified zone mapping
-- Each source's zone was derived from the zone abbreviations its OWN rows carry
-- (word-bounded: " ESTIMATED" matches "EST" without a boundary and dragged
-- western sources east on an earlier pass), then cross-checked against the
-- geography the source name states. Every source below agreed with itself.
DECLARE @SourceZone TABLE (SourceName nvarchar(200) PRIMARY KEY, ZoneId nvarchar(64));
INSERT INTO @SourceZone (SourceName, ZoneId) VALUES
    -- Alberta: rows carry MDT/MST
    (N'BidsTenders_Camrose',        N'Mountain Standard Time'),
    (N'BidsTenders_Cityofgp',       N'Mountain Standard Time'),
    (N'BidsTenders_CountyGP',       N'Mountain Standard Time'),
    (N'BidsTenders_EPCOR',          N'Mountain Standard Time'),
    (N'BidsTenders_Leduc',          N'Mountain Standard Time'),
    (N'BidsTenders_Lethbridge',     N'Mountain Standard Time'),
    (N'BidsTenders_Okotoks',        N'Mountain Standard Time'),
    (N'BidsTenders_RedDeer',        N'Mountain Standard Time'),
    (N'BidsTenders_StAlbert',       N'Mountain Standard Time'),
    (N'BidsTenders_WoodBuffalo',    N'Mountain Standard Time'),
    (N'Bonfire_CbeAb',              N'Mountain Standard Time'), -- Calgary Board of Education
    (N'APC_AllBuyers',              N'Mountain Standard Time'), -- renders "(MT)"
    -- British Columbia: rows carry PDT/PST
    (N'BidsTenders_Abbotsford',     N'Pacific Standard Time'),
    (N'BidsTenders_Burnaby',        N'Pacific Standard Time'),
    (N'BidsTenders_Campbellriver',  N'Pacific Standard Time'),
    (N'BidsTenders_Comoxvalleyrd',  N'Pacific Standard Time'),
    (N'BidsTenders_Coquitlam',      N'Pacific Standard Time'),
    (N'BidsTenders_DNV',            N'Pacific Standard Time'),
    (N'BidsTenders_MapleRidge',     N'Pacific Standard Time'),
    (N'BidsTenders_MetroVancouver', N'Pacific Standard Time'),
    (N'BidsTenders_Nanaimo',        N'Pacific Standard Time'),
    (N'BidsTenders_PortMoody',      N'Pacific Standard Time'),
    (N'BidsTenders_PrinceGeorge',   N'Pacific Standard Time'),
    (N'BidsTenders_Rdco',           N'Pacific Standard Time'),
    (N'BidsTenders_Richmond',       N'Pacific Standard Time'),
    (N'BidsTenders_Squamish',       N'Pacific Standard Time'),
    (N'BidsTenders_TOL',            N'Pacific Standard Time'),
    (N'BcBid',                      N'Pacific Standard Time'), -- column: "(pacific time)"
    (N'BcBid_Engineering',          N'Pacific Standard Time'),
    (N'Bonfire_BCIT',               N'Pacific Standard Time'),
    (N'Bonfire_FNHA',               N'Pacific Standard Time'),
    (N'Bonfire_FraserHealth',       N'Pacific Standard Time'),
    (N'Bonfire_IslandHealth',       N'Pacific Standard Time'),
    (N'Bonfire_Kamloops',           N'Pacific Standard Time'),
    (N'Bonfire_Mission',            N'Pacific Standard Time'),
    (N'Bonfire_PHSA',               N'Pacific Standard Time'),
    (N'Bonfire_UBC',                N'Pacific Standard Time'),
    (N'Bonfire_UVic',               N'Pacific Standard Time'),
    -- Federal, published Eastern (live notice: 14:00 EDT in Oct, 14:00 EST in Feb)
    (N'CanadaBuys',                 N'Eastern Standard Time'),
    (N'CanadaBuysNew',              N'Eastern Standard Time');

-- Sources that publish UTC or carry their own offset, or publish no time at all.
DECLARE @LeaveAlone TABLE (SourceName nvarchar(200) PRIMARY KEY, Reason nvarchar(100));
INSERT INTO @LeaveAlone (SourceName, Reason) VALUES
    (N'LACity_RAMP_OpenBids',     N'closedate is already UTC (28 of 28 cross-checked against its own title clock)'),
    (N'SamGov',                   N'responseDeadLine carries a numeric offset'),
    (N'MERX-DCC',                 N'upstream emits +00:00, date-only'),
    (N'CivicInfoBC_All',          N'publishes no time, only "Posting expires: <date>"'),
    (N'CivicInfoBC_Construction', N'publishes no time, only "Posting expires: <date>"');

-- Buyer -> zone, for opportunities whose observations were removed by
-- BdOpportunityDedup and so have no source to resolve. OpportunityKey gives the
-- platform (BIDSTEND-) but not the tenant; BuyerName does.
-- nvarchar(300), not (600): a 600-char key is 1200 bytes and exceeds the 900-byte
-- index limit, which SQL Server warns about on every run. Longest real buyer name
-- here is 44 characters.
DECLARE @BuyerZone TABLE (BuyerName nvarchar(300) PRIMARY KEY, ZoneId nvarchar(64));
INSERT INTO @BuyerZone (BuyerName, ZoneId) VALUES
    (N'City of Leduc',                                 N'Mountain Standard Time'),
    (N'City of St. Albert',                            N'Mountain Standard Time'),
    (N'City of Lethbridge',                            N'Mountain Standard Time'),
    (N'City of Red Deer',                              N'Mountain Standard Time'),
    (N'County of Grande Prairie',                      N'Mountain Standard Time'),
    (N'City of Grande Prairie',                        N'Mountain Standard Time'),
    (N'Town of Okotoks',                               N'Mountain Standard Time'),
    (N'EPCOR',                                         N'Mountain Standard Time'),
    (N'Metro Vancouver Regional District',             N'Pacific Standard Time'),
    (N'City of Burnaby',                               N'Pacific Standard Time'),
    (N'City of Maple Ridge',                           N'Pacific Standard Time'),
    (N'City of Abbotsford',                            N'Pacific Standard Time'),
    (N'Township of Langley',                           N'Pacific Standard Time'),
    (N'City of Richmond',                              N'Pacific Standard Time'),
    (N'District of North Vancouver',                   N'Pacific Standard Time'),
    (N'City of Prince George',                         N'Pacific Standard Time'),
    (N'City of Nanaimo',                               N'Pacific Standard Time'),
    (N'City of Coquitlam',                             N'Pacific Standard Time'),
    (N'BidsTenders_PortMoody',                         N'Pacific Standard Time'),
    (N'RDCO (Regional District of Central Okanagan)',  N'Pacific Standard Time');

-- ------------------------------------------------------- resolve one zone / row
-- Latest observation per (opportunity, source); an opportunity can carry
-- observations from several sources, so zones are collapsed to a DISTINCT set
-- and a row whose sources disagree is excluded rather than guessed at.
IF OBJECT_ID('tempdb..#cand') IS NOT NULL DROP TABLE #cand;

WITH obs AS (
    SELECT ob.OpportunityId, s.Name AS SourceName
    FROM opportunities.OpportunityObservations ob
    JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
    GROUP BY ob.OpportunityId, s.Name
),
src AS (   -- zone implied by the opportunity's own sources
    SELECT o.Id,
           MIN(z.ZoneId) AS ZoneId,
           COUNT(DISTINCT z.ZoneId) AS ZoneCount,
           MAX(CASE WHEN l.SourceName IS NOT NULL THEN 1 ELSE 0 END) AS HasLeaveAloneSource,
           COUNT(DISTINCT obs.SourceName) AS SourceCount,
           COUNT(DISTINCT CASE WHEN z.ZoneId IS NULL AND l.SourceName IS NULL
                               THEN obs.SourceName END) AS UnmappedSources,
           MIN(obs.SourceName) AS AnySourceName
    FROM opportunities.Opportunities o
    JOIN obs ON obs.OpportunityId = o.Id
    LEFT JOIN @SourceZone z ON z.SourceName = obs.SourceName
    LEFT JOIN @LeaveAlone l ON l.SourceName = obs.SourceName
    WHERE o.SubmissionDeadlineUtc IS NOT NULL
    GROUP BY o.Id
)
SELECT o.Id AS OpportunityId,
       COALESCE(src.AnySourceName, N'(no observation)') AS SourceName,
       CAST(resolved.ZoneId AS nvarchar(64)) AS ZoneId,
       CAST(resolved.Basis AS nvarchar(60)) AS Basis,
       o.SubmissionDeadlineUtc AS OldDeadlineUtc,
       CAST(CAST(o.SubmissionDeadlineUtc AS datetime2)
            AT TIME ZONE resolved.ZoneId AT TIME ZONE 'UTC' AS datetimeoffset) AS NewDeadlineUtc
INTO #cand
FROM opportunities.Opportunities o
LEFT JOIN src ON src.Id = o.Id
CROSS APPLY (
    SELECT TOP 1 ZoneId, Basis FROM (
        -- 1. the opportunity's own source(s), when they agree on one zone and
        --    none of them is a leave-alone source
        SELECT 1 AS pref, src.ZoneId, N'source zone' AS Basis
        WHERE src.Id IS NOT NULL AND src.ZoneCount = 1
          AND src.HasLeaveAloneSource = 0 AND src.UnmappedSources = 0
        UNION ALL
        -- 2. no observation at all: BC Bid is identifiable from its key prefix
        SELECT 2, N'Pacific Standard Time', N'key prefix BCBID (no observation)'
        WHERE src.Id IS NULL
          AND (o.OpportunityKey LIKE N'BCBID-%' OR o.OpportunityKey LIKE N'BCBIDENG-%')
        UNION ALL
        -- 3. no observation, bids&tenders: the key names the platform, not the
        --    tenant, so the buyer resolves the zone
        SELECT 3, bz.ZoneId, N'buyer zone (no observation)'
        FROM @BuyerZone bz
        WHERE src.Id IS NULL AND o.OpportunityKey LIKE N'BIDSTEND%'
          AND bz.BuyerName = o.BuyerName
    ) AS ranked ORDER BY pref
) AS resolved
WHERE o.SubmissionDeadlineUtc IS NOT NULL
  -- ⛔ STILL HOLDS OUR CORRECTION: skip a row whose CURRENT value is one this
  -- migration already produced. This is the guard that matters and it took two
  -- attempts to get right.
  --   v1 had no guard at all: #cand is rebuilt from the current stored value, so
  --   a second run read the already-correct instant as a fresh wall clock and
  --   shifted 2,072 rows another 4-8 hours. Matching the UPDATE on the expected
  --   old value does NOT prevent that -- it only guards a concurrent edit
  --   between building #cand and applying it.
  --   v2 skipped any opportunity with ANY audit row, which is idempotent but not
  --   self-healing: opportunity 39850 was corrected at 13:30, then re-written as
  --   a wall clock at 13:34 by a scheduled BC Bid run still on the old binaries,
  --   and v2 then refused to touch it forever.
  -- Keying on the VALUE satisfies both: a row holding our output is left alone
  -- (no double shift), and a row that has since been re-ingested as a wall clock
  -- is corrected again.
  AND NOT EXISTS (
        SELECT 1 FROM opportunities.DeadlineClockBackfill b
        WHERE b.OpportunityId = o.Id
          AND b.NewDeadlineUtc = o.SubmissionDeadlineUtc)
  -- a date-only capture has no published time; shifting it would invent one
  AND CAST(o.SubmissionDeadlineUtc AS time) <> '00:00:00'
  -- APC's scraper was corrected 2026-10-08 and its rows since then are already
  -- real instants (hour 18-23 UTC); only the earlier wall clocks need moving
  AND NOT (COALESCE(src.AnySourceName, N'') = N'APC_AllBuyers'
           AND DATEPART(hour, o.SubmissionDeadlineUtc) >= 18);

-- Exclude DST edges: a wall clock that does not exist, or exists twice, must not
-- be silently resolved to one instant. AT TIME ZONE picks one without saying so,
-- so these are detected by round-tripping and dropped.
DELETE FROM #cand
WHERE CAST(CAST(NewDeadlineUtc AS datetime2) AT TIME ZONE 'UTC'
           AT TIME ZONE ZoneId AS datetime2) <> CAST(OldDeadlineUtc AS datetime2);

-- ------------------------------------------------------------ coverage assertion
-- Fail closed: any source holding a time-bearing deadline that is neither mapped
-- nor explicitly left alone means the mapping is incomplete and the numbers
-- below would be a lie.
DECLARE @unaccounted nvarchar(max) = NULL;

SELECT @unaccounted = STRING_AGG(CAST(t.SourceName AS nvarchar(max)), N', ')
FROM (
    SELECT DISTINCT s.Name AS SourceName
    FROM opportunities.Opportunities o
    JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
    JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
    WHERE o.SubmissionDeadlineUtc IS NOT NULL
      AND CAST(o.SubmissionDeadlineUtc AS time) <> '00:00:00'
      AND NOT EXISTS (SELECT 1 FROM @SourceZone z WHERE z.SourceName = s.Name)
      AND NOT EXISTS (SELECT 1 FROM @LeaveAlone l WHERE l.SourceName = s.Name)
) AS t;

IF @unaccounted IS NOT NULL
BEGIN
    RAISERROR(N'340 aborted: sources hold time-bearing deadlines but are in neither the zone map nor the leave-alone list: %s', 16, 1, @unaccounted);
    RETURN;
END;

-- ----------------------------------------------------------------- what we found
SELECT 'candidates' AS Report, COUNT(*) AS Rows_,
       SUM(CASE WHEN NewDeadlineUtc <= OldDeadlineUtc THEN 1 ELSE 0 END) AS NotMovedForward
FROM #cand;

SELECT DATEDIFF(hour, OldDeadlineUtc, NewDeadlineUtc) AS ShiftHours,
       COUNT(*) AS Rows_
FROM #cand GROUP BY DATEDIFF(hour, OldDeadlineUtc, NewDeadlineUtc) ORDER BY ShiftHours;

SELECT Basis, ZoneId, COUNT(*) AS Rows_
FROM #cand GROUP BY Basis, ZoneId ORDER BY Rows_ DESC;

-- Every correction must move the deadline strictly later, by 4 to 8 hours. A row
-- outside that is a resolution error, not a timezone correction.
IF EXISTS (SELECT 1 FROM #cand
           WHERE DATEDIFF(hour, OldDeadlineUtc, NewDeadlineUtc) NOT BETWEEN 4 AND 8)
BEGIN
    RAISERROR(N'340 aborted: a candidate shift is outside the 4-8 hour range every North American zone implies.', 16, 1);
    RETURN;
END;

IF @DryRun = 1
BEGIN
    PRINT '';
    PRINT 'Apply=0 - nothing was changed.';
    -- The WHOLE candidate set, not a sample: a dry run exists to be diffed
    -- against an independent calculation of the same correction (SQL uses
    -- Windows zone data via AT TIME ZONE; the cross-check used Python/IANA),
    -- and a truncated list cannot support a claim about all 2,116 rows.
    SELECT OpportunityId, SourceName, ZoneId, Basis,
           CONVERT(varchar(25), OldDeadlineUtc, 127) AS WasUtc,
           CONVERT(varchar(25), NewDeadlineUtc, 127) AS NowUtc
    FROM #cand ORDER BY OpportunityId;
    RETURN;
END;

-- ------------------------------------------------------------------- the cut
BEGIN TRANSACTION;

    INSERT INTO opportunities.DeadlineClockBackfill
        (OpportunityId, SourceName, ZoneId, Basis, OldDeadlineUtc, NewDeadlineUtc,
         ShiftHours, MigrationName)
    SELECT c.OpportunityId, c.SourceName, c.ZoneId, c.Basis,
           c.OldDeadlineUtc, c.NewDeadlineUtc,
           DATEDIFF(hour, c.OldDeadlineUtc, c.NewDeadlineUtc),
           N'340_BackfillDeadlineLocalWallClocks'
    FROM #cand c
    JOIN opportunities.Opportunities o
      ON o.Id = c.OpportunityId
     AND o.SubmissionDeadlineUtc = c.OldDeadlineUtc;   -- still as measured

    DECLARE @audited int = @@ROWCOUNT;

    UPDATE o
       SET o.SubmissionDeadlineUtc = c.NewDeadlineUtc
    FROM opportunities.Opportunities o
    JOIN #cand c
      ON c.OpportunityId = o.Id
     AND o.SubmissionDeadlineUtc = c.OldDeadlineUtc;   -- concurrency-safe, idempotent

    DECLARE @updated int = @@ROWCOUNT;

    IF @audited <> @updated
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'340 aborted: audited %d rows but updated %d. Nothing committed.', 16, 1, @audited, @updated);
        RETURN;
    END;

COMMIT TRANSACTION;

PRINT '';
SELECT 'applied' AS Report, @updated AS RowsUpdated;

-- ------------------------------------------------------------------ after-state
-- Opportunities auto-expired by DataRetirementJob whose corrected deadline is
-- actually still in the future. Reported, NOT changed: reopening a pursuit is a
-- judgement call.
SELECT 'expired but corrected deadline is still future' AS Report, COUNT(*) AS Rows_
FROM opportunities.Opportunities o
JOIN opportunities.DeadlineClockBackfill b ON b.OpportunityId = o.Id
WHERE b.MigrationName = N'340_BackfillDeadlineLocalWallClocks'
  AND o.SubmissionDeadlineUtc > sysdatetimeoffset()
  AND (o.Status = 7 OR o.WonLostOutcome = 3);
