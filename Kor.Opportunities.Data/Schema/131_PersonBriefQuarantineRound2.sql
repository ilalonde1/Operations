-- 131_PersonBriefQuarantineRound2.sql
-- Completes the m125 residue: the 38-row "over id 200" review. Classified
-- by relaxed evidence (>= 2 name parts present in content): 24 are
-- name-format false positives ("Joseph (Joe) Mayo" class) and stay; 16
-- fail even relaxed evidence -> truly misattributed; same quarantine
-- treatment as m125 (Status flag + decomposed intel retired).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRAN;

DECLARE @Bad TABLE (PersonId bigint PRIMARY KEY);
INSERT INTO @Bad (PersonId) VALUES (295),(296),(297),(298),(299),(319),(320),(357),(358),(362),(363),(373),(507),(521),(848),(3882);

-- Guard: only quarantine rows that STILL fail strict name match (idempotent
-- and safe if a correct brief landed since classification).
UPDATE e SET Status = N'Misattributed',
             Notes = COALESCE(e.Notes + NCHAR(13) + NCHAR(10), N'') + N'[m131: failed relaxed name-evidence review (m125 residue)]',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.CanonicalOrgEnrichment e
JOIN opportunities.IntelPerson p ON p.Id = TRY_CAST(SUBSTRING(e.ProviderName,13,20) AS bigint)
JOIN @Bad b ON b.PersonId = p.Id
WHERE e.ProviderName LIKE N'PersonBrief-%' AND e.ProviderName NOT LIKE N'PersonBriefHoning-%'
  AND e.Status = N'Ok'
  AND e.ResultJson NOT LIKE N'%' + p.DisplayName + N'%';
PRINT 'Quarantined: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm131: decomposed from a misattributed PersonBrief (review residue)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelSignal x JOIN @Bad b ON x.SourceProviderName = N'PersonBrief-' + CAST(b.PersonId AS nvarchar(12))
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Signals retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm131: decomposed from a misattributed PersonBrief (review residue)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelAction x JOIN @Bad b ON x.SourceProviderName = N'PersonBrief-' + CAST(b.PersonId AS nvarchar(12))
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Actions retired: ' + CAST(@@ROWCOUNT AS varchar(10));
UPDATE x SET RetiredAtUtc = sysdatetimeoffset(),
             RetiredReason = N'm131: decomposed from a misattributed PersonBrief (review residue)',
             UpdatedAtUtc = sysdatetimeoffset()
FROM opportunities.IntelPersonAffiliation x JOIN @Bad b ON x.SourceProviderName = N'PersonBrief-' + CAST(b.PersonId AS nvarchar(12))
WHERE x.RetiredAtUtc IS NULL;
PRINT 'Affiliations retired: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm131 committed.';
GO
