-- 125_PersonBriefQuarantine.sql
-- People-brief misattribution repair, part 2 (part 1 = the on-disk rekey via
-- tools/BdPersonBriefRepair + the name-resolving ingest, commit 2ed7ac2).
--
-- PersonBrief-{1..200} enrichment rows were written against batch ORDINALS,
-- attributing research to whichever IntelPerson owned those ids. The rekey
-- re-ingested every recoverable brief under its TRUE person; this migration
-- quarantines the surviving WRONG rows so dossiers and selectors stop
-- consuming them:
--   * CanonicalOrgEnrichment rows where the keyed person's DisplayName does
--     not appear in the brief content (the heuristic that scoped the
--     incident: ~192/196 precision in the 1-200 range) -> Status flag
--     'Misattributed' (consumers filter Status = 'Ok').
--   * Intel rows decomposed FROM those briefs (SourceProviderName =
--     'PersonBrief-{n}') -> retired with reason.
-- Scope is capped at PersonId <= 200: above that the same heuristic is
-- dominated by name-format false positives (38 rows, left for review).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

DECLARE @Bad TABLE (EnrichmentId bigint PRIMARY KEY, PersonId bigint NOT NULL);
INSERT INTO @Bad (EnrichmentId, PersonId)
SELECT e.Id, p.Id
FROM opportunities.CanonicalOrgEnrichment e
JOIN opportunities.IntelPerson p
  ON p.Id = TRY_CAST(SUBSTRING(e.ProviderName, 13, 20) AS bigint)
WHERE e.ProviderName LIKE N'PersonBrief-%'
  AND e.ProviderName NOT LIKE N'PersonBriefHoning-%'
  AND e.ResultJson IS NOT NULL
  AND p.Id <= 200
  AND e.ResultJson NOT LIKE N'%' + p.DisplayName + N'%'
  AND e.Status = N'Ok';

DECLARE @badCount int;
SELECT @badCount = COUNT(*) FROM @Bad;
PRINT 'Misattributed enrichment rows found: ' + CAST(@badCount AS varchar(10));

UPDATE e SET Status = N'Misattributed',
             Notes = COALESCE(e.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m125: ordinal-id misattribution — content is NOT about IntelPerson ' + CAST(b.PersonId AS nvarchar(12)) + N'; true subject re-ingested from disk where recoverable]',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrgEnrichment e JOIN @Bad b ON b.EnrichmentId = e.Id;
PRINT 'Enrichment rows quarantined: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Intel rows decomposed from the wrong-keyed briefs.
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm125: decomposed from a misattributed PersonBrief (ordinal-id incident)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelSignal x
JOIN @Bad b ON x.SourceProviderName = N'PersonBrief-' + CAST(b.PersonId AS nvarchar(12))
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelSignal retired: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm125: decomposed from a misattributed PersonBrief (ordinal-id incident)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelAction x
JOIN @Bad b ON x.SourceProviderName = N'PersonBrief-' + CAST(b.PersonId AS nvarchar(12))
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelAction retired: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm125: decomposed from a misattributed PersonBrief (ordinal-id incident)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation x
JOIN @Bad b ON x.SourceProviderName = N'PersonBrief-' + CAST(b.PersonId AS nvarchar(12))
WHERE x.RetiredAtUtc IS NULL;
PRINT 'IntelPersonAffiliation retired: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm125 committed.';
GO

-- Verify: zero Status='Ok' PersonBrief rows in the 1-200 range whose content
-- does not mention its person.
SELECT COUNT(*) AS StillMisattributedOk
FROM opportunities.CanonicalOrgEnrichment e
JOIN opportunities.IntelPerson p ON p.Id = TRY_CAST(SUBSTRING(e.ProviderName, 13, 20) AS bigint)
WHERE e.ProviderName LIKE N'PersonBrief-%' AND e.ProviderName NOT LIKE N'PersonBriefHoning-%'
  AND e.ResultJson IS NOT NULL AND p.Id <= 200 AND e.Status = N'Ok'
  AND e.ResultJson NOT LIKE N'%' + p.DisplayName + N'%';
GO
