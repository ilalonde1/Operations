SET XACT_ABORT ON;
GO

-- Source cleanup for the 22 placeholder CanonicalOrg rows that had been
-- created by the scraper when it couldn't identify the real entity
-- (e.g., a CanonicalOrg named "TBD (architect not yet identified)" was
-- being attached as the Architect FK on MPI rows).
--
-- Bucket A — 14 pure placeholders ("Unknown", "TBD", "TBD (Colliers as
-- owner's rep)" etc.): orphan by nulling every MPI / Opps FK that points
-- to them AND nulling the corresponding display column. Brief queries
-- then honestly show "(not yet known)" for those roles rather than
-- "Unknown".
--
-- Bucket B — 8 real firms with parenthetical or OCR noise (e.g.,
-- "Patkau Architects (Presentation Centre); community centre architect
-- TBD" -> "Patkau Architects", "G>A> Leblanc Construction" ->
-- "G.A. Leblanc Construction"): rename DisplayName, then resync any
-- MPI / Opps display columns that reference them.
--
-- CanonicalOrg has no RetiredAtUtc column today, so Bucket A is done by
-- orphan-and-leave (placeholder rows become dangling but unreachable).

BEGIN TRAN;

DECLARE @junkOrgIds TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @junkOrgIds VALUES
    (72411), -- "TBD (design-build architect not yet publicly identified)"
    (72430), -- "TBD (Kaigo as DBOM operator — internal or contracted design team...)"
    (72426), -- "TBD (not publicly identified)"
    (72159), -- "Unknown — Architectural Services RFP closed April 9, 2026"
    (72147), -- "Unknown (design contract awarded; $13M architect contract)"
    (72088), -- "Developer TBD (City incentive program)"
    (72136), -- "Unknown" (Competitor) — 125 MPI refs
    (72195), -- "TBD" (GC) — 52 MPI refs
    (72208), -- "TBD (Colliers Project Leaders as owner's rep)"
    (72412), -- "TBD (design-build contractor not yet publicly named)"
    (72427), -- "TBD (not publicly named)"
    (72143), -- "Unknown (design-build RFP issued 2026)"
    (72154), -- "Unknown (proponent selected 2024)"
    (63552); -- "SPARKES KENNETH Commissionaire NDHQ..." (a person mistakenly created as a Buyer org)

-- ========== Bucket A — orphan the 14 pure placeholders ==========

DECLARE @architectNulled int, @structEngNulled int, @gcNulled int,
        @proponentNulled int, @buyerNulled int;

UPDATE m SET m.ArchitectCanonicalOrgId = NULL, m.ArchitectName = NULL
FROM opportunities.MajorProjectsInventory m
WHERE m.ArchitectCanonicalOrgId IN (SELECT Id FROM @junkOrgIds);
SET @architectNulled = @@ROWCOUNT;
PRINT 'MPI Architect FK+name cleared: ' + CONVERT(varchar(20), @architectNulled);

UPDATE m SET m.StructuralEngineerCanonicalOrgId = NULL, m.StructuralEngineerName = NULL
FROM opportunities.MajorProjectsInventory m
WHERE m.StructuralEngineerCanonicalOrgId IN (SELECT Id FROM @junkOrgIds);
SET @structEngNulled = @@ROWCOUNT;
PRINT 'MPI StructEng FK+name cleared: ' + CONVERT(varchar(20), @structEngNulled);

UPDATE m SET m.GeneralContractorCanonicalOrgId = NULL, m.GeneralContractorName = NULL
FROM opportunities.MajorProjectsInventory m
WHERE m.GeneralContractorCanonicalOrgId IN (SELECT Id FROM @junkOrgIds);
SET @gcNulled = @@ROWCOUNT;
PRINT 'MPI GC FK+name cleared: ' + CONVERT(varchar(20), @gcNulled);

UPDATE m SET m.ProponentCanonicalOrgId = NULL, m.ProponentName = NULL
FROM opportunities.MajorProjectsInventory m
WHERE m.ProponentCanonicalOrgId IN (SELECT Id FROM @junkOrgIds);
SET @proponentNulled = @@ROWCOUNT;
PRINT 'MPI Proponent FK+name cleared: ' + CONVERT(varchar(20), @proponentNulled);

UPDATE o SET o.BuyerCanonicalOrgId = NULL, o.BuyerName = NULL
FROM opportunities.Opportunities o
WHERE o.BuyerCanonicalOrgId IN (SELECT Id FROM @junkOrgIds);
SET @buyerNulled = @@ROWCOUNT;
PRINT 'Opps Buyer FK+name cleared: ' + CONVERT(varchar(20), @buyerNulled);

-- ========== Bucket B — rename 8 real firms ==========

UPDATE opportunities.CanonicalOrg SET DisplayName = N'Civitas Urban Design & Architecture'
WHERE Id = 69862;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'Patkau Architects'
WHERE Id = 72248;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'Perkins+Will'
WHERE Id = 71269;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'Assisted Living Alberta'
WHERE Id IN (72092, 72094);
UPDATE opportunities.CanonicalOrg SET DisplayName = N'G.A. Leblanc Construction & Services Ltd.'
WHERE Id = 48825;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'T.M. One Construction Ltd'
WHERE Id = 31747;
UPDATE opportunities.CanonicalOrg SET DisplayName = N'TBD Architecture + Urban Planning'
WHERE Id = 73991;
PRINT '8 real-firm CanonicalOrgs renamed to clean DisplayName.';

-- Resync MPI display columns from canonical for the renamed orgs.
DECLARE @renamedIds TABLE (Id BIGINT PRIMARY KEY);
INSERT INTO @renamedIds VALUES (69862), (72248), (71269), (72092), (72094), (48825), (31747), (73991);

UPDATE m SET m.ArchitectName = co.DisplayName
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg co ON co.Id = m.ArchitectCanonicalOrgId
WHERE m.ArchitectCanonicalOrgId IN (SELECT Id FROM @renamedIds);
PRINT 'MPI Architect display synced from renamed orgs: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m SET m.StructuralEngineerName = co.DisplayName
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg co ON co.Id = m.StructuralEngineerCanonicalOrgId
WHERE m.StructuralEngineerCanonicalOrgId IN (SELECT Id FROM @renamedIds);
PRINT 'MPI StructEng display synced: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m SET m.GeneralContractorName = co.DisplayName
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg co ON co.Id = m.GeneralContractorCanonicalOrgId
WHERE m.GeneralContractorCanonicalOrgId IN (SELECT Id FROM @renamedIds);
PRINT 'MPI GC display synced: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE m SET m.ProponentName = co.DisplayName
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.CanonicalOrg co ON co.Id = m.ProponentCanonicalOrgId
WHERE m.ProponentCanonicalOrgId IN (SELECT Id FROM @renamedIds);
PRINT 'MPI Proponent display synced: ' + CONVERT(varchar(20), @@ROWCOUNT);

UPDATE o SET o.BuyerName = co.DisplayName
FROM opportunities.Opportunities o
INNER JOIN opportunities.CanonicalOrg co ON co.Id = o.BuyerCanonicalOrgId
WHERE o.BuyerCanonicalOrgId IN (SELECT Id FROM @renamedIds);
PRINT 'Opps Buyer display synced: ' + CONVERT(varchar(20), @@ROWCOUNT);

PRINT 'Migration 73 placeholder org cleanup complete.';

COMMIT TRAN;
GO
