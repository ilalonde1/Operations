USE [KorOpportunitiesDb];
GO

/* =====================================================================
   Adds full-team columns to MajorProjectsInventory so the architect <->
   structural-engineer <-> GC pairing graph is RELATIONAL (queryable), not
   buried in RawJson. Populated by BdResearchImport's Intel-Gathering Track A
   (team-awards). Mirrors the existing Architect* columns.

   These reveal, per project: which structural firm an architect teamed with
   (who KOR would displace) and which architects rotate structural firms.
   ===================================================================== */
IF COL_LENGTH('opportunities.MajorProjectsInventory', 'StructuralEngineerName') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD StructuralEngineerName nvarchar(500) NULL;
GO
IF COL_LENGTH('opportunities.MajorProjectsInventory', 'StructuralEngineerCanonicalOrgId') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD StructuralEngineerCanonicalOrgId bigint NULL;
GO
IF COL_LENGTH('opportunities.MajorProjectsInventory', 'GeneralContractorName') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD GeneralContractorName nvarchar(500) NULL;
GO
IF COL_LENGTH('opportunities.MajorProjectsInventory', 'GeneralContractorCanonicalOrgId') IS NULL
    ALTER TABLE opportunities.MajorProjectsInventory ADD GeneralContractorCanonicalOrgId bigint NULL;
GO

-- Index to answer "which structural firm did this architect team with" fast.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MPI_ArchitectStructural'
               AND object_id = OBJECT_ID('opportunities.MajorProjectsInventory'))
    CREATE INDEX IX_MPI_ArchitectStructural
        ON opportunities.MajorProjectsInventory (ArchitectCanonicalOrgId, StructuralEngineerCanonicalOrgId)
        WHERE ArchitectCanonicalOrgId IS NOT NULL;
GO

PRINT 'Migration 46: MPI team columns (structural engineer + GC name/canonical) added.';
GO
