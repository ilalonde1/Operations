SET XACT_ABORT ON;
GO

-- Backfill junk display names from the canonical-org FK that's already set.
-- The string columns (ProponentName / ArchitectName / etc.) had stale or
-- placeholder values from the original scrape, but the FK to CanonicalOrg
-- carries the real entity. Sync the displays back to the canonical name.

BEGIN TRAN;

DECLARE @proponentFixed int;
DECLARE @architectFixed int;
DECLARE @structEngFixed int;
DECLARE @gcFixed int;
DECLARE @buyerFixed int;

-- Junk predicate (inline for clarity; same word-boundary semantics as the
-- brief filter, but APPLIED HERE at source so the brief queries stay clean).
-- Junk = NULL OR starts with 'unknown' OR contains standalone TBD/TBA OR
-- contains angle brackets.

UPDATE m
SET m.ProponentName = co.DisplayName
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg co ON co.Id = m.ProponentCanonicalOrgId
WHERE m.RetiredAtUtc IS NULL
  AND m.ProponentCanonicalOrgId IS NOT NULL
  AND (
       m.ProponentName IS NULL
    OR LOWER(m.ProponentName) LIKE N'unknown%'
    OR PATINDEX(N'%[^a-z]tbd[^a-z]%', N' ' + LOWER(m.ProponentName) + N' ') > 0
    OR PATINDEX(N'%[^a-z]tba[^a-z]%', N' ' + LOWER(m.ProponentName) + N' ') > 0
    OR m.ProponentName LIKE N'%<%'
    OR m.ProponentName LIKE N'%>%'
  );
SET @proponentFixed = @@ROWCOUNT;
PRINT 'MPI ProponentName backfilled from FK: ' + CONVERT(varchar(20), @proponentFixed) + ' rows';

UPDATE m
SET m.ArchitectName = co.DisplayName
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg co ON co.Id = m.ArchitectCanonicalOrgId
WHERE m.RetiredAtUtc IS NULL
  AND m.ArchitectCanonicalOrgId IS NOT NULL
  AND (
       LOWER(m.ArchitectName) LIKE N'unknown%'
    OR PATINDEX(N'%[^a-z]tbd[^a-z]%', N' ' + LOWER(m.ArchitectName) + N' ') > 0
    OR PATINDEX(N'%[^a-z]tba[^a-z]%', N' ' + LOWER(m.ArchitectName) + N' ') > 0
    OR m.ArchitectName LIKE N'%<%'
    OR m.ArchitectName LIKE N'%>%'
  );
SET @architectFixed = @@ROWCOUNT;
PRINT 'MPI ArchitectName backfilled from FK: ' + CONVERT(varchar(20), @architectFixed) + ' rows';

UPDATE m
SET m.StructuralEngineerName = co.DisplayName
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg co ON co.Id = m.StructuralEngineerCanonicalOrgId
WHERE m.RetiredAtUtc IS NULL
  AND m.StructuralEngineerCanonicalOrgId IS NOT NULL
  AND (
       LOWER(m.StructuralEngineerName) LIKE N'unknown%'
    OR PATINDEX(N'%[^a-z]tbd[^a-z]%', N' ' + LOWER(m.StructuralEngineerName) + N' ') > 0
    OR PATINDEX(N'%[^a-z]tba[^a-z]%', N' ' + LOWER(m.StructuralEngineerName) + N' ') > 0
    OR m.StructuralEngineerName LIKE N'%<%'
    OR m.StructuralEngineerName LIKE N'%>%'
  );
SET @structEngFixed = @@ROWCOUNT;
PRINT 'MPI StructuralEngineerName backfilled from FK: ' + CONVERT(varchar(20), @structEngFixed) + ' rows';

UPDATE m
SET m.GeneralContractorName = co.DisplayName
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg co ON co.Id = m.GeneralContractorCanonicalOrgId
WHERE m.RetiredAtUtc IS NULL
  AND m.GeneralContractorCanonicalOrgId IS NOT NULL
  AND (
       LOWER(m.GeneralContractorName) LIKE N'unknown%'
    OR PATINDEX(N'%[^a-z]tbd[^a-z]%', N' ' + LOWER(m.GeneralContractorName) + N' ') > 0
    OR PATINDEX(N'%[^a-z]tba[^a-z]%', N' ' + LOWER(m.GeneralContractorName) + N' ') > 0
    OR m.GeneralContractorName LIKE N'%<%'
    OR m.GeneralContractorName LIKE N'%>%'
  );
SET @gcFixed = @@ROWCOUNT;
PRINT 'MPI GeneralContractorName backfilled from FK: ' + CONVERT(varchar(20), @gcFixed) + ' rows';

UPDATE o
SET o.BuyerName = co.DisplayName
FROM opportunities.Opportunities o
INNER JOIN opportunities.CanonicalOrg co ON co.Id = o.BuyerCanonicalOrgId
WHERE o.Status = 1
  AND o.BuyerCanonicalOrgId IS NOT NULL
  AND (
       o.BuyerName IS NULL
    OR LOWER(o.BuyerName) LIKE N'unknown%'
    OR PATINDEX(N'%[^a-z]tbd[^a-z]%', N' ' + LOWER(o.BuyerName) + N' ') > 0
    OR PATINDEX(N'%[^a-z]tba[^a-z]%', N' ' + LOWER(o.BuyerName) + N' ') > 0
    OR o.BuyerName LIKE N'%<%'
    OR o.BuyerName LIKE N'%>%'
  );
SET @buyerFixed = @@ROWCOUNT;
PRINT 'Opps BuyerName backfilled from FK: ' + CONVERT(varchar(20), @buyerFixed) + ' rows';

PRINT 'Migration 72 FK-backfill complete. Total rows fixed at source: '
    + CONVERT(varchar(20), @proponentFixed + @architectFixed + @structEngFixed + @gcFixed + @buyerFixed);

COMMIT TRAN;
GO
