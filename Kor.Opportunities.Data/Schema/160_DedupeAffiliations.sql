USE [KorOpportunitiesDb];
GO

/* =====================================================================
   160 — Dedupe IntelPersonAffiliation: one live row per (person, org).
   ---------------------------------------------------------------------
   Re-extraction created up to 6 live affiliation rows for the same person at
   the same org (different Title strings) — ~3,494 redundant rows that clutter
   every contact list / call sheet. Keep the richest row (has a Title, then
   longest Title, then lowest Id); retire the rest (soft delete, audit-safe).
   ===================================================================== */

WITH ranked AS (
  SELECT a.Id,
    ROW_NUMBER() OVER (
      PARTITION BY a.IntelPersonId, a.CanonicalOrgId
      ORDER BY CASE WHEN NULLIF(LTRIM(RTRIM(a.Title)),'') IS NOT NULL THEN 0 ELSE 1 END,
               LEN(ISNULL(a.Title,'')) DESC,
               a.Id ASC) AS rn
  FROM opportunities.IntelPersonAffiliation a
  WHERE a.RetiredAtUtc IS NULL)
UPDATE a
SET a.RetiredAtUtc = SYSDATETIMEOFFSET(),
    a.RetiredReason = N'Duplicate affiliation (same person+org) — kept richest (migration 160)',
    a.UpdatedAtUtc = SYSDATETIMEOFFSET()
FROM opportunities.IntelPersonAffiliation a
JOIN ranked r ON r.Id = a.Id
WHERE r.rn > 1;
GO
