/* Connection-scoped like every other migration — no USE.
   APPLIED to KOR-APP01 KorOpportunitiesDb 2026-07-11 (verified 0 live
   duplicate NormalizedNames before creation). */

/* =====================================================================
   279 — Filtered unique index: one LIVE CanonicalOrg per NormalizedName.
   ---------------------------------------------------------------------
   The twin factory's last door: every code guard can be bypassed by a new
   write path, but this index makes a live strict-name duplicate physically
   impossible. Retired rows are exempt (history keeps its duplicates); the
   resolver's duplicate-key catch (SqlCanonicalOrgStore.UpsertCanonicalOrgAsync)
   already re-finds and returns the winner on a lost race.
   ===================================================================== */

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID('opportunities.CanonicalOrg')
                 AND name = 'UX_CanonicalOrg_LiveNormalizedName')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_CanonicalOrg_LiveNormalizedName
        ON opportunities.CanonicalOrg (NormalizedName)
        WHERE RetiredAtUtc IS NULL;
END;
