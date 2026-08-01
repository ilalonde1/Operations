USE [KorOpportunitiesDb];
GO

/* =====================================================================
   137 — Restore the frozen KorStructural self-anchor.
   ---------------------------------------------------------------------
   KOR's own canonical row (Id 38918, "KOR Structural Ltd.", ~100 MPI
   references, 11 enrichment rows) had lost its frozen `KorStructural`
   kind to a dedup collapse and was sitting as a generic `Firm`. The
   KorCapability importer (BdResearchImport `ResolveKorStructuralOrgAsync`)
   hard-throws "Could not find the KOR Structural canonical org row" when
   no `Kind = 'KorStructural'` row exists, which aborts the entire
   multi-tag research import sweep.

   This restores the anchor so the resolver's primary kind-search finds it
   (FROZEN_KINDS then protects it from importer downgrade going forward),
   and retires two junk KOR-variant rows that carried 0 MPI references:
     - 76290 = self-reference auto-minted by the 2026-06-15 sweep,
               mislabeled `Competitor` (polluted competitor analysis).
     - 18817 = APC email-notification artifact, mislabeled `Buyer`.

   Idempotent: re-running is a no-op once the anchor kind is set and the
   junk rows are retired. Archive-not-delete per the retirement lifecycle.
   ===================================================================== */

UPDATE opportunities.CanonicalOrg
SET    Kind = N'KorStructural'
WHERE  Id = 38918
  AND  Kind <> N'KorStructural';
GO

UPDATE opportunities.CanonicalOrg
SET    RetiredAtUtc = SYSUTCDATETIME()
WHERE  Id IN (76290, 18817)
  AND  RetiredAtUtc IS NULL;
GO
