USE [KorOpportunitiesDb];
GO

/* =====================================================================
   153 — Decompose multi-firm composite org rows + retire the junk org names.
   ---------------------------------------------------------------------
   86 live CanonicalOrg rows have semicolon DisplayNames that stuff a whole
   project team into one "org" ("Kasian (master plan); Durante Kreuk (landscape)").
   They are scrape/research narratives, not real single organizations.

   Only 5 of them sit in a role column on an ACTIVE MPI row (all Architect role);
   the rest only touch retired/historical MPI. The 5 active ones are decomposed to
   their PRIMARY (lead) firm's canonical; then every composite org row is retired
   as non-canonical.

   Active decompositions (MpiId -> primary architect):
     6336  100 Street Pedestrian Bridge   -> Grimshaw (628; lead design architect)
     7026  VGH Campus Master Plan          -> Kasian  (70543; master-plan lead)
     7027  Lions Gate Hospital Health Vision -> Arcadis IBI Group (153; master-plan lead)
     4209  Foothills Multisport Fieldhouse -> GGA Architecture (8129; only named firm)
     6923  Onni 375 East 1st Ave           -> NULL (composite is a status note,
                                              "architect not yet public")
   Grimshaw (628) was mislabeled Kind='Buyer' — corrected to Architect.

   Retire guard: keeps any Deltek-linked (ClendorClientId) row; the frozen-anchor
   trigger (139) protects KorStructural/KorClient. Idempotent.
   ===================================================================== */

/* 1. Decompose the 5 active Architect placements to their primary firm. */
UPDATE opportunities.MajorProjectsInventory SET ArchitectCanonicalOrgId = 628,   UpdatedAtUtc = SYSDATETIMEOFFSET() WHERE Id = 6336 AND ArchitectCanonicalOrgId = 71183;
UPDATE opportunities.MajorProjectsInventory SET ArchitectCanonicalOrgId = 70543, UpdatedAtUtc = SYSDATETIMEOFFSET() WHERE Id = 7026 AND ArchitectCanonicalOrgId = 72418;
UPDATE opportunities.MajorProjectsInventory SET ArchitectCanonicalOrgId = 153,   UpdatedAtUtc = SYSDATETIMEOFFSET() WHERE Id = 7027 AND ArchitectCanonicalOrgId = 72419;
UPDATE opportunities.MajorProjectsInventory SET ArchitectCanonicalOrgId = 8129,  UpdatedAtUtc = SYSDATETIMEOFFSET() WHERE Id = 4209 AND ArchitectCanonicalOrgId = 76306;
UPDATE opportunities.MajorProjectsInventory SET ArchitectCanonicalOrgId = NULL,  UpdatedAtUtc = SYSDATETIMEOFFSET() WHERE Id = 6923 AND ArchitectCanonicalOrgId = 72241;

/* 2. Fix Grimshaw's mislabeled Kind (it is an architecture firm, not a Buyer). */
UPDATE opportunities.CanonicalOrg SET Kind = N'Architect', UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 628 AND Kind = N'Buyer';

/* 3. Retire every composite org row (now that the 5 active placements are repointed,
      none has an active MPI reference). Preserve any Deltek-linked row. */
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = SYSDATETIMEOFFSET(),
    RetiredReason = N'Multi-firm composite narrative — not a canonical org (migration 153)'
WHERE RetiredAtUtc IS NULL
  AND DisplayName LIKE N'%;%'
  AND ClendorClientId IS NULL
  AND NOT EXISTS (
    SELECT 1 FROM opportunities.MajorProjectsInventory m
    WHERE m.RetiredAtUtc IS NULL
      AND (m.ArchitectCanonicalOrgId = opportunities.CanonicalOrg.Id
        OR m.StructuralEngineerCanonicalOrgId = opportunities.CanonicalOrg.Id
        OR m.GeneralContractorCanonicalOrgId = opportunities.CanonicalOrg.Id
        OR m.ProponentCanonicalOrgId = opportunities.CanonicalOrg.Id));
GO
