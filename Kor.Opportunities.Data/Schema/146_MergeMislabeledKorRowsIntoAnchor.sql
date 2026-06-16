USE [KorOpportunitiesDb];
GO

/* =====================================================================
   146 — Merge mislabeled "KOR" rows into the frozen KOR anchor (38918).
   ---------------------------------------------------------------------
   Three live CanonicalOrg rows were created as Kind='Competitor' for KOR
   itself, splitting KOR's own structural-engineering foot-in off the frozen
   self-anchor (38918 "KOR Structural Ltd.", Kind=KorStructural):

       76502  KOR Structural Engineers                        (94 MPI SE refs)
       76558  KOR Structural                                  ( 1 MPI SE ref)
       76290  KOR Structural (One Burrard Place — per portfolio)( 0 refs)

   Consequence: every report read "KOR foot-in: 0" because the 95 projects
   where KOR is the structural engineer pointed at competitor-labeled junk
   rows, not the anchor. (StructuralEngineerCanonicalOrgId is not FK-
   constrained, so the schema-driven dedup CLI does not catch this — hence a
   targeted, audited merge.)

   Reference scan (all 26 referencing columns) found the junk ids only in:
       MajorProjectsInventory.StructuralEngineerCanonicalOrgId  95   -> repoint
       CanonicalOrgEnrichment.CanonicalOrgId                     3   -> delete (anchor keeps its own 11)
       OrgAlias.CanonicalOrgId                                   4   -> repoint (free alias resolution to anchor)

   The 3 junk rows are Kind='Competitor' (NOT frozen), so retiring them does
   not trip the 139 frozen-anchor guard. Idempotent: re-running is a no-op.
   ===================================================================== */

DECLARE @anchor BIGINT = 38918;
DECLARE @junk TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @junk (Id) VALUES (76502), (76558), (76290);

/* Safety: confirm the anchor is exactly the frozen KOR self-row before touching anything. */
IF NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrg
               WHERE Id = @anchor AND Kind = N'KorStructural' AND RetiredAtUtc IS NULL)
BEGIN
    RAISERROR('Anchor 38918 is not the live KorStructural row — aborting merge.', 16, 1);
    RETURN;
END

/* 1. Repoint the 95 structural-engineer project refs to the anchor. */
UPDATE m
SET m.StructuralEngineerCanonicalOrgId = @anchor
FROM opportunities.MajorProjectsInventory m
WHERE m.StructuralEngineerCanonicalOrgId IN (SELECT Id FROM @junk);

/* 2. Repoint existing aliases to the anchor (they already name KOR variants). */
UPDATE a
SET a.CanonicalOrgId = @anchor
FROM opportunities.OrgAlias a
WHERE a.CanonicalOrgId IN (SELECT Id FROM @junk);

/* 3. Seed each junk DisplayName as an alias of the anchor (idempotent) so future
      imports of "KOR Structural Engineers" etc. resolve to the anchor, not a new row. */
INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, Confidence, ClassifiedBy, ClassifiedAtUtc, Notes, CreatedAtUtc)
SELECT @anchor, j.DisplayName, N'Merge.146', 100, N'migration-146', SYSDATETIMEOFFSET(),
       N'Mislabeled KOR self-row merged into frozen anchor', SYSDATETIMEOFFSET()
FROM opportunities.CanonicalOrg j
WHERE j.Id IN (SELECT Id FROM @junk)
  AND NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias x
                  WHERE x.CanonicalOrgId = @anchor AND x.RawName = j.DisplayName);

/* 4. Delete the duplicate enrichment rows on the junk orgs (anchor keeps its own). */
DELETE FROM opportunities.CanonicalOrgEnrichment
WHERE CanonicalOrgId IN (SELECT Id FROM @junk);

/* 5. Retire the 3 junk rows (soft delete; they are Kind='Competitor', not frozen). */
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = SYSDATETIMEOFFSET()
WHERE Id IN (SELECT Id FROM @junk) AND RetiredAtUtc IS NULL;
GO
