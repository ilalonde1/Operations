/* Connection-scoped like every other migration — no USE. Run against the
   intended database's connection.
   APPLIED to KOR-APP01 KorOpportunitiesDb 2026-07-11. */

/* =====================================================================
   278 — SeatStatus vocabulary backfill: 'Open' -> 'likely-open'.
   ---------------------------------------------------------------------
   Migrations 235/240 stamped SeatStatus = 'Open' (including live tier-1 warm
   pursuits) but every open-seat consumer — dashboard counts, filters, the MCP
   recipe — matches only the lowercase vocabulary ('likely-open'), so the
   highest-value open-seat rows were invisible to all of them.

   BdResearchImport now normalizes SeatStatus at write time (audit-v2 #9), so
   this backfill cannot regress. Vocabulary: unknown / filled / likely-open /
   locked.
   ===================================================================== */

UPDATE opportunities.MajorProjectsInventory
SET SeatStatus = N'likely-open',
    UpdatedAtUtc = sysdatetimeoffset()
WHERE SeatStatus = N'Open' COLLATE Latin1_General_CS_AS;

-- Belt-and-suspenders: lowercase any other cased variants of known values.
UPDATE opportunities.MajorProjectsInventory
SET SeatStatus = LOWER(SeatStatus),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE SeatStatus <> LOWER(SeatStatus) COLLATE Latin1_General_CS_AS
  AND LOWER(SeatStatus) IN (N'unknown', N'filled', N'likely-open', N'locked');
