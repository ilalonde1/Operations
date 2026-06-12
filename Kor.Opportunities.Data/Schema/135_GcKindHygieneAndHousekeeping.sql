-- 135_GcKindHygieneAndHousekeeping.sql
-- Closes the remaining evidence-backed items of
-- docs/BD-SummaryFlags-Worklist-2026-06-12.md: the honing-gcs b001/b002
-- GC kind-hygiene lists (the m132 pattern applied to the GC kind), plus
-- financially-flagged retires and singles. Evidence: honing-gcs
-- SUMMARY-batch-001/002.txt + per-org refresh-org-<id>.json briefs
-- (FirmNarrativeHoning, 2026-06-11..12). Names were resolved to ids against
-- live CanonicalOrg 2026-06-12; only unambiguous GC-kind matches included.
-- Merge-class outcomes (16 pairs incl. Stuart Olson -> Bird) go through
-- BdCanonicalDedup --pairs, not this file.
-- No DB rows found for: O'Hanlon Paving, Chappy's Contracting, Rossco's
-- Tree Service (nothing to reclassify).
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRAN;

DECLARE @now datetimeoffset = sysdatetimeoffset();

-- honing-gcs b002 'DISMISS (civil/paving/earthworks — not building GCs)'
-- (ids verbatim from the summary) + b001 'Civil/highway/utility/demolition
-- (wrong sector)' list (names resolved to ids). Same treatment m133 gave
-- misclassified kinds: Kind -> Unknown + enrichment suppression.
UPDATE co SET Kind = N'Unknown',
  EnrichmentSuppressedAtUtc = COALESCE(co.EnrichmentSuppressedAtUtc, @now),
  EnrichmentSuppressedReason = COALESCE(co.EnrichmentSuppressedReason, N'm135: civil/paving/earthworks — not a building GC per honing-gcs brief'),
  UpdatedAtUtc = @now
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL AND co.Id IN (
  -- b002 ids: D. Owen, DeFord, Brazel, Breycon, BROCOR, Broersen, Ant JV,
  -- Blue Bird, Blue Flame, Black River, E Construction, East Butte,
  -- Greenfield, Kidco, North Star, Noyen, Kichton, Central Construction,
  -- Cat Bros Oilfield, CDM, M. Umscheid, A.B. Hollingworth, A.I.C., ACP,
  -- Aecon Transportation West, Al Saunders, Alberco (bridge GC), AH Grader,
  -- AICEIM (shallow utility), AJ Wieler, A&R Contracting, Hilliars,
  -- Klassen, 51 North, D-Kazz, A & J, A Two Z
  5738, 5961, 3682, 3711, 3748, 3751, 2418, 3453, 3455, 3407, 6517, 6574,
  8479, 10318, 12526, 12643, 10311, 4589, 4460, 4523, 11173, 1508, 1513,
  1691, 1832, 1969, 1977, 1911, 1914, 1953, 1503, 1153, 1015, 1370, 1379,
  1485, 1498,
  -- b001 names resolved: Volker Stevin GC rows, McNally x4, Lahrmann,
  -- Standard General, Paveit x2, Whissel x2, Sandstar x2, Rock Iron,
  -- Rulam x5, Sure Form, Jimcor, Vortrax, KSN x4, Professional
  -- Excavators x2, RLS, Waterworks, Coastal Restoration x4, AA Sturgeon
  71597, 17948, 17951, 11573, 11574, 11575, 11576, 10652, 15900, 71938,
  13220, 18401, 18402, 14939, 71689, 14608, 14787, 14788, 71610, 14789,
  14791, 16303, 48047, 17957, 10545, 10546, 10548, 10549, 13766, 13768,
  49590, 46735, 46732, 46844, 47052, 47270, 47455);
PRINT 'm135 civil/wrong-sector reclassified: ' + CAST(@@ROWCOUNT AS varchar(10));

-- honing-gcs b001 'Residential/small GCs (wrong market)' (names resolved;
-- composite 54449 'North American Development Group/ Kerkhoff' excluded —
-- composite-name records are planner fodder, not kind errors).
UPDATE co SET Kind = N'Unknown',
  EnrichmentSuppressedAtUtc = COALESCE(co.EnrichmentSuppressedAtUtc, @now),
  EnrichmentSuppressedReason = COALESCE(co.EnrichmentSuppressedReason, N'm135: residential/small GC — wrong market per honing-gcs b001 brief'),
  UpdatedAtUtc = @now
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL AND co.Id IN (
  54740, 53889, 53761, 54487, 70544, 48545, 46714, 63627, 47175, 46054,
  47695, 47795, 46703, 49825, 18473, 47253, 47301, 47409, 54936, 73674,
  69665, 70559, 70736, 48540, 70241, 17998, 18253, 48084, 10379, 11773,
  46956, 15748, 13353, 71901, 14814, 14832, 49823);
PRINT 'm135 residential/wrong-market reclassified: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Trade subs mis-kinded as GC (b001 'wrong discipline' + b002 CCS
-- reclassify): truthful kind is Subcontractor.
-- 17554 Unitech (electrical), 17892 Visco (demolition; 17891 already
-- Subcontractor), 49874 Atlantica (mechanical), 4504 CCS (building
-- envelope per b002), 2645 Arte Roofing (roofing, from b002 dismiss list).
UPDATE co SET Kind = N'Subcontractor', UpdatedAtUtc = @now
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL AND co.Kind = N'GC' AND co.Id IN (17554, 17892, 49874, 4504, 2645);
PRINT 'm135 trade subs re-kinded: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Roads West Engineering (14564): highway/roads engineering firm — the
-- garbled twin 14565 carries tender text 'Project B 12:18 from Hwy. 872...'
-- (merged into 14564 in the pairs run). Allied-discipline, not a
-- structural competitor.
UPDATE co SET Kind = N'Unknown',
  EnrichmentSuppressedAtUtc = COALESCE(co.EnrichmentSuppressedAtUtc, @now),
  EnrichmentSuppressedReason = COALESCE(co.EnrichmentSuppressedReason, N'm135: highway/roads engineering — allied-discipline (summary-flags worklist)'),
  UpdatedAtUtc = @now
FROM opportunities.CanonicalOrg co
WHERE co.Id = 14564 AND co.RetiredAtUtc IS NULL;
PRINT 'm135 Roads West reclassified: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Celtic Construction (3922): b002 confirms the entity is Carl Chandler's
-- Celtic Construction (Dawson Creek/Grande Prairie); current DisplayName is
-- a concatenation artifact. Dup 3921 merges into it in the pairs run.
UPDATE opportunities.CanonicalOrg
SET DisplayName = N'Celtic Construction', UpdatedAtUtc = @now
WHERE Id = 3922 AND DisplayName = N'C. Chandler Contracting Celtic Construction' AND RetiredAtUtc IS NULL;
PRINT 'm135 Celtic rename: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Financially flagged (b001/b002):
UPDATE opportunities.CanonicalOrg
SET RetiredAtUtc = @now,
  RetiredReason = CASE Id
    WHEN 6118  THEN N'm135: Devlin Construction IN RECEIVERSHIP (honing-gcs b002)'
    WHEN 46615 THEN N'm135: RGT Clouthier bankrupt (honing-gcs b001)'
    WHEN 47813 THEN N'm135: RGT Clouthier bankrupt (honing-gcs b001) — vendor-row sibling of 46615'
  END,
  UpdatedAtUtc = @now
WHERE Id IN (6118, 46615, 47813) AND RetiredAtUtc IS NULL;
PRINT 'm135 financially-flagged retired: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Man-Shield (11302): operating but CBC-reported non-payment court cases
-- (Thunder Bay) — note, do not retire.
UPDATE opportunities.CanonicalOrg
SET Notes = COALESCE(Notes + N' ', N'') + N'[m135: PAYMENT RISK — CBC-reported non-payment court cases (Thunder Bay), honing-gcs b002. Do not pursue without credit check.]',
    UpdatedAtUtc = @now
WHERE Id = 11302 AND RetiredAtUtc IS NULL;
PRINT 'm135 Man-Shield risk-noted: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRAN;
PRINT 'm135 committed.';
GO

SELECT Kind, COUNT(*) AS n FROM opportunities.CanonicalOrg
WHERE Id IN (17554, 17892, 49874, 4504, 2645, 14564, 3922, 6118, 46615, 47813, 11302)
GROUP BY Kind;
GO
