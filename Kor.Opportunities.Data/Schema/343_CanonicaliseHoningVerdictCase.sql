-- 343: canonicalise lower-case honing verdicts written by on-demand synthesis.
--
-- OnDemandHoningSynthesizer persisted the model's token verbatim, so 88 active
-- ProjectBriefHoning rows (87 `pursue`, 1 `monitor`; all the LATEST row for their
-- project; all with Notes starting 'on-demand synthesis') carry a lower-case verdict
-- at $.honingPass.verdict. The C# readers switch on the exact token, so these rows
-- fell out of every PURSUE count (BD audit finding 3). Brief 2 fixed the writer and
-- made every reader normalise; this repairs the stored value so the data agrees with
-- the vocabulary and tools/BdIntegrityCheck bd_report_noncanonical_verdict reads 0.
--
-- Idempotent: keyed on the VALUE (a canonical token present in the wrong case); a
-- repaired row no longer matches. A token that is not canonical in ANY case (say
-- 'banana') is reported and never rewritten -- that would be a different defect.
--
-- Undo: opportunities.HoningVerdictCaseLog holds EnrichmentId, path and old value.
--
-- HOW TO RUN
--   report only:  sqlcmd ... -i 343_CanonicaliseHoningVerdictCase.sql -v Apply=0
--   apply:        sqlcmd ... -i 343_CanonicaliseHoningVerdictCase.sql -v Apply=1

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @DryRun bit = CASE WHEN $(Apply) = 1 THEN 0 ELSE 1 END;
DECLARE @migration nvarchar(100) = N'343_CanonicaliseHoningVerdictCase';

IF OBJECT_ID('opportunities.HoningVerdictCaseLog') IS NULL
BEGIN
    CREATE TABLE opportunities.HoningVerdictCaseLog (
        Id            bigint IDENTITY(1,1) PRIMARY KEY,
        EnrichmentId  bigint         NOT NULL,
        JsonPath      nvarchar(64)   NOT NULL,
        OldVerdict    nvarchar(64)   NOT NULL,
        NewVerdict    nvarchar(64)   NOT NULL,
        AppliedAtUtc  datetimeoffset NOT NULL DEFAULT sysdatetimeoffset(),
        MigrationName nvarchar(100)  NOT NULL
    );

    EXEC sys.sp_addextendedproperty
        @name = N'Rollback',
        @value = N'For each HoningVerdictCaseLog row with MigrationName 343_CanonicaliseHoningVerdictCase: UPDATE opportunities.MajorProjectEnrichment SET ResultJson = JSON_MODIFY(ResultJson, <JsonPath literal>, OldVerdict) WHERE Id = EnrichmentId. Two paths exist ($.honingPass.verdict, $.verdict); run one UPDATE per path.',
        @level0type = N'SCHEMA', @level0name = N'opportunities',
        @level1type = N'TABLE',  @level1name = N'HoningVerdictCaseLog';
END;

IF OBJECT_ID('tempdb..#fix') IS NOT NULL DROP TABLE #fix;

-- One row per (enrichment, path) whose value is a canonical token in the wrong case.
SELECT e.Id AS EnrichmentId, p.JsonPath, p.Verdict AS OldVerdict, UPPER(p.Verdict) AS NewVerdict
INTO #fix
FROM opportunities.MajorProjectEnrichment e
CROSS APPLY (VALUES
    (N'$.honingPass.verdict', JSON_VALUE(e.ResultJson, '$.honingPass.verdict')),
    (N'$.verdict',            JSON_VALUE(e.ResultJson, '$.verdict'))) p(JsonPath, Verdict)
WHERE e.ProviderName = N'ProjectBriefHoning'
  AND p.Verdict IS NOT NULL
  AND UPPER(p.Verdict) COLLATE Latin1_General_100_BIN2
      IN (N'PURSUE_URGENT', N'PURSUE', N'MONITOR', N'DISCOVER', N'DEAD', N'DUPLICATE')
  AND p.Verdict COLLATE Latin1_General_100_BIN2 <> UPPER(p.Verdict) COLLATE Latin1_General_100_BIN2;

SELECT 'to canonicalise' AS Report, COUNT(*) AS Rows_ FROM #fix;
SELECT JsonPath, OldVerdict, NewVerdict, COUNT(*) AS Rows_
FROM #fix GROUP BY JsonPath, OldVerdict, NewVerdict ORDER BY JsonPath, OldVerdict;

-- Reported, never touched: a token that is not canonical in any case.
SELECT 'not a canonical token (left alone)' AS Report, p.Verdict, COUNT(*) AS Rows_
FROM opportunities.MajorProjectEnrichment e
CROSS APPLY (VALUES
    (JSON_VALUE(e.ResultJson, '$.honingPass.verdict')),
    (JSON_VALUE(e.ResultJson, '$.verdict'))) p(Verdict)
WHERE e.ProviderName = N'ProjectBriefHoning'
  AND p.Verdict IS NOT NULL
  AND UPPER(p.Verdict) COLLATE Latin1_General_100_BIN2
      NOT IN (N'PURSUE_URGENT', N'PURSUE', N'MONITOR', N'DISCOVER', N'DEAD', N'DUPLICATE')
GROUP BY p.Verdict;

IF @DryRun = 1
BEGIN
    PRINT '';
    PRINT 'Apply=0 - nothing was changed.';
    RETURN;
END;

BEGIN TRANSACTION;

    INSERT INTO opportunities.HoningVerdictCaseLog
        (EnrichmentId, JsonPath, OldVerdict, NewVerdict, MigrationName)
    SELECT EnrichmentId, JsonPath, OldVerdict, NewVerdict, @migration FROM #fix;
    DECLARE @logged int = @@ROWCOUNT;

    -- JSON_MODIFY takes a literal path, so one UPDATE per path.
    UPDATE e
       SET e.ResultJson   = JSON_MODIFY(e.ResultJson, '$.honingPass.verdict', f.NewVerdict),
           e.UpdatedAtUtc = sysdatetimeoffset()
    FROM opportunities.MajorProjectEnrichment e
    JOIN #fix f ON f.EnrichmentId = e.Id AND f.JsonPath = N'$.honingPass.verdict'
    WHERE JSON_VALUE(e.ResultJson, '$.honingPass.verdict') COLLATE Latin1_General_100_BIN2 = f.OldVerdict COLLATE Latin1_General_100_BIN2;
    DECLARE @fixed int = @@ROWCOUNT;

    UPDATE e
       SET e.ResultJson   = JSON_MODIFY(e.ResultJson, '$.verdict', f.NewVerdict),
           e.UpdatedAtUtc = sysdatetimeoffset()
    FROM opportunities.MajorProjectEnrichment e
    JOIN #fix f ON f.EnrichmentId = e.Id AND f.JsonPath = N'$.verdict'
    WHERE JSON_VALUE(e.ResultJson, '$.verdict') COLLATE Latin1_General_100_BIN2 = f.OldVerdict COLLATE Latin1_General_100_BIN2;
    SET @fixed = @fixed + @@ROWCOUNT;

    IF @logged <> @fixed
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'343 aborted: logged %d but rewrote %d. Nothing committed.', 16, 1, @logged, @fixed);
        RETURN;
    END;

    -- Every logged row must now read canonical, or nothing is committed.
    IF EXISTS (
        SELECT 1
        FROM #fix f
        JOIN opportunities.MajorProjectEnrichment e ON e.Id = f.EnrichmentId
        WHERE COALESCE(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'), JSON_VALUE(e.ResultJson, '$.verdict'))
              COLLATE Latin1_General_100_BIN2 <> f.NewVerdict COLLATE Latin1_General_100_BIN2)
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'343 aborted: a rewritten row still reads non-canonical. Nothing committed.', 16, 1);
        RETURN;
    END;

COMMIT TRANSACTION;

SELECT 'canonicalised' AS Report, @fixed AS Rows_;
