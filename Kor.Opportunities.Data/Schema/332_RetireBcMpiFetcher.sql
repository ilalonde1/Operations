-- Migration 332 (2026-10-08): retire the BC Major Projects Inventory FETCHER.
--   The 741 BC rows it produced are NOT touched. This disables a dead fetcher.
--
-- WHY. The new §J MPI-freshness check (migration-free, DataHealthAuditJob.SourceFreshness)
-- flagged BC_MajorProjectsInventory "fetch overdue" on its first run. Investigating that
-- found two separate faults, and only one of them was already known:
--
--   1. THE FETCHER IS BROKEN, NOT QUIET. The CKAN package is still `state: active` and
--      was `metadata_modified: 2026-09-22` — it was restructured, not withdrawn, and its
--      CSV resource was REMOVED. Its three resources are now BCGW Custom Download, WMS
--      and KML. So the provider hard-fails:
--
--        2026-09-20  Success=1
--        2026-09-27  Success=0  "BC Major Projects Inventory CKAN response did not
--                                contain a CSV resource."
--        2026-10-04  Success=0  same
--
--   2. THE DATA BEHIND IT IS FROZEN, so the obvious repoint is a trap. The replacement
--      BCGW layer works and is RICHER than the CSV — WFS GeoJSON, 648 features, 41 fields
--      including ARCHITECT, DEVELOPER, ESTIMATED_COST, PROJECT_STAGE. But ALL 648 rows
--      carry LAST_UPDATE = 2024-12-01, and the newest FIRST_ENTRY_DATE is also 2024-12 —
--      OLDER than the Q1-2025 snapshot we had been replaying weekly. Repointing would buy
--      a green feed serving ten-month-old data, which is the exact
--      "reports success it has not earned" family that
--      reference_bc_mpi_discontinued_2026 was written about.
--
-- AND THERE IS NOTHING TO REPOINT TO. `package_search?q=major projects inventory` on the
-- BC catalogue returns 1 of 5 matches that is the MPI, and it is that same frozen package.
-- BC Stats discontinued the programme; the layer is a static archive.
--
-- NOTHING IS LOST BY RETIRING IT. The frozen layer names an architect on 200 of 648 rows;
-- we already hold 216 architects on our 741 BC-* rows (249 live), from honing and research.
-- Those rows stay queryable. A retired fetcher is not an abandoned region.
--
-- WHAT THIS CHANGES, stated so the next reader is not surprised:
--   * Q9 already excluded type 18, so §I is unaffected.
--   * §J's denominator goes 6 -> 5 enabled type-18 sources, and the permanent unclearable
--     "REVIEW — fetch overdue" row for BC disappears. A review prompt that can never be
--     resolved is noise, and noise is how a real alert gets ignored.
--
-- WHAT THIS DOES NOT DO. It does not replace the capability. MPI was a province-wide
-- forward pipeline; its replacement is the municipal early-signal layer, where the gap is
-- measured in docs/KOR-EarlySignal-CoverageGap-2026-09-10.md: 13 of 56 in-footprint
-- municipalities, Metro Vancouver 4 of 21, Fraser Valley 0 of 6. That is the real work and
-- it is not this migration.
USE [KorOpportunitiesDb];
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

SELECT 'before' AS Section,
       s.Name, s.SourceType, s.IsEnabled, s.IsHistorical,
       (SELECT COUNT_BIG(*) FROM opportunities.MajorProjectsInventory m
        WHERE m.Province = N'BC' AND LEFT(m.SourceKey, 3) = N'BC-')               AS BcProviderRows,
       (SELECT COUNT_BIG(*) FROM opportunities.MajorProjectsInventory m
        WHERE m.Province = N'BC' AND LEFT(m.SourceKey, 3) = N'BC-'
          AND m.RetiredAtUtc IS NULL)                                             AS BcProviderRowsLive
FROM opportunities.OpportunitySources s
WHERE s.Name = N'BC_MajorProjectsInventory';
GO

UPDATE opportunities.OpportunitySources
SET IsEnabled    = 0,
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Name = N'BC_MajorProjectsInventory'
  AND IsEnabled = 1;
GO

SELECT 'after' AS Section,
       s.Name, s.SourceType, s.IsEnabled,
       (SELECT COUNT_BIG(*) FROM opportunities.MajorProjectsInventory m
        WHERE m.Province = N'BC' AND LEFT(m.SourceKey, 3) = N'BC-')               AS BcProviderRowsUnchanged
FROM opportunities.OpportunitySources s
WHERE s.Name = N'BC_MajorProjectsInventory';
GO

-- The §J denominator after this change, so the next report's header is predictable.
SELECT 'section J universe after retirement' AS Section,
       COUNT_BIG(*) AS EnabledType18Sources
FROM opportunities.OpportunitySources
WHERE IsEnabled = 1 AND SourceType = 18;
GO
