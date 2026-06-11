-- 126_OrgEnrichmentSuppression.sql
-- "Do not enrich" mechanism for CanonicalOrg (Ian, 2026-06-11): the org
-- research selectors already kind-gate to the six BD kinds, but junk-named
-- orgs INSIDE those kinds pass the generator, burn a batch slot, get
-- session-skipped for garbled names — and, never gaining enrichment, are
-- re-selected forever (observed live: orgs batch-015 skipped 36/59).
--
-- Mechanism: nullable EnrichmentSuppressedAtUtc + reason on CanonicalOrg.
-- The orgs / honing-orgs selectors in Generate-Batch.ps1 add
-- `AND co.EnrichmentSuppressedAtUtc IS NULL`. Suppression is reversible
-- (NULL the column) and orthogonal to retirement — suppressed orgs stay
-- valid link targets for awards/projects; they just never get research
-- slots.
--
-- Initial sweep: junk-name heuristics (multi-entity separators,
-- parenthetical descriptors, placeholders, single-token/short, shouty-caps)
-- over un-enriched orgs of the six enrichable kinds, plus the 36 concrete
-- batch-015 session skips (truncated names the heuristics can't express).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('opportunities.CanonicalOrg', 'EnrichmentSuppressedAtUtc') IS NULL
BEGIN
    ALTER TABLE opportunities.CanonicalOrg
        ADD EnrichmentSuppressedAtUtc datetimeoffset NULL,
            EnrichmentSuppressedReason nvarchar(200) NULL;
END
GO

BEGIN TRAN;

-- Heuristic sweep (same name-quality rules the drain sessions apply).
UPDATE co SET
    EnrichmentSuppressedAtUtc = sysdatetimeoffset(),
    EnrichmentSuppressedReason = N'm126: junk-name sweep — garbled/multi-entity/placeholder name; fix the name to re-enable'
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND co.EnrichmentSuppressedAtUtc IS NULL
  AND co.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor', N'KorClient')
  AND (
       co.DisplayName LIKE N'%/%' OR co.DisplayName LIKE N'%;%' OR co.DisplayName LIKE N'%|%'
    OR co.DisplayName LIKE N'%(%'
    OR LEN(co.DisplayName) < 6
    OR co.DisplayName NOT LIKE N'% %'
    OR co.DisplayName LIKE N'% AND' OR co.DisplayName LIKE N'% OR' OR co.DisplayName LIKE N'%,'
    OR (co.DisplayName COLLATE Latin1_General_CS_AS = UPPER(co.DisplayName COLLATE Latin1_General_CS_AS) AND LEN(co.DisplayName) > 12)
    OR co.DisplayName LIKE N'%TBD%' OR co.DisplayName LIKE N'%TBA%'
    OR co.DisplayName LIKE N'%unknown%' OR co.DisplayName LIKE N'%unidentified%'
  )
  AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                  WHERE e.CanonicalOrgId = co.Id
                    AND e.ProviderName = N'FirmNarrative' AND e.ResultJson IS NOT NULL);
PRINT 'Heuristic sweep suppressed: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Concrete batch-015 session skips (truncated DisplayNames — un-researchable
-- as written; the heuristics cannot express mid-word truncation).
UPDATE co SET
    EnrichmentSuppressedAtUtc = sysdatetimeoffset(),
    EnrichmentSuppressedReason = N'm126: orgs batch-015 session skip — truncated/garbled name; fix the name to re-enable'
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND co.EnrichmentSuppressedAtUtc IS NULL
  AND co.Id IN (71367, 66459, 70102, 66354, 12864, 27879, 25338, 38590, 66818, 74057, 13380, 25041,
                13587, 23258, 24120, 71121, 70105, 27247, 14023, 19095, 70101, 61459, 34010, 52020,
                51754, 69136, 52537, 61440, 66298, 70147, 71154, 26717, 26493, 70160, 70537, 33413);
PRINT 'Batch-015 skips suppressed: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm126 committed.';
GO

-- Verify: suppression totals by kind.
SELECT co.Kind, COUNT(*) AS Suppressed
FROM opportunities.CanonicalOrg co
WHERE co.EnrichmentSuppressedAtUtc IS NOT NULL
GROUP BY co.Kind ORDER BY Suppressed DESC;
GO
