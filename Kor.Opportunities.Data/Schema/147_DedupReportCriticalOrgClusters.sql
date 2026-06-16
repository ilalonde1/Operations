USE [KorOpportunitiesDb];
GO

/* =====================================================================
   147 — Canonicalize the report-critical dedup survivors.
   ---------------------------------------------------------------------
   The merges themselves are performed by the established, battle-tested
   BdCanonicalDedup --pairs engine (report-critical-pairs-2026-06-16.csv,
   gated by dedup-non-similar-allowlist.csv), which handles FK repointing
   and all collision tables (enrichment unique key, aliases, affiliations,
   intel dedup, KorProjectsCount rollup). This migration only normalizes the
   surviving rows' display names / websites afterwards so reports read clean.

   Survivors:
     38926  Glotman Simpson Consulting Engineers   (name already clean)
     69332  WHM Structural Engineers               (name already clean)
     76162  Vancouver School Board (SD39)          <- renamed here
     69571  West Vancouver School District (SD45)  <- renamed here
     75852  North Vancouver School District (SD44) <- renamed here

   Idempotent. Run AFTER the --pairs merge commits.
   ===================================================================== */

UPDATE opportunities.CanonicalOrg
SET DisplayName = N'Vancouver School Board (SD39)',
    Website = COALESCE(NULLIF(Website, N''), N'https://www.vsb.bc.ca/'),
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 76162 AND RetiredAtUtc IS NULL;

UPDATE opportunities.CanonicalOrg
SET DisplayName = N'West Vancouver School District (SD45)',
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 69571 AND RetiredAtUtc IS NULL;

UPDATE opportunities.CanonicalOrg
SET DisplayName = N'North Vancouver School District (SD44)',
    Website = COALESCE(NULLIF(Website, N''), N'https://www.sd44.ca/'),
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 75852 AND RetiredAtUtc IS NULL;
GO
