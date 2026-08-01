/*
    Kor.OpportunitiesDb migration 47.

    Audit T3.002 (2026-05-30): migration 46 added
    StructuralEngineerCanonicalOrgId and GeneralContractorCanonicalOrgId columns
    plus a composite index filtered on ArchitectCanonicalOrgId, but did NOT add
    single-column filtered indexes on each new FK column. Queries that search
    by structural engineer alone (e.g. SqlCanonicalOrgStore's
    SearchCanonicalOrgsWithRelationshipsAsync after the T1.003 fix) or by GC
    alone fall back to table scans without these.

    Idempotent. Each index gated on sys.indexes existence check.
*/

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_MPI_StructuralEngineerCanonicalOrgId'
                 AND object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
BEGIN
    CREATE INDEX IX_MPI_StructuralEngineerCanonicalOrgId
        ON opportunities.MajorProjectsInventory (StructuralEngineerCanonicalOrgId)
        WHERE StructuralEngineerCanonicalOrgId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_MPI_GeneralContractorCanonicalOrgId'
                 AND object_id = OBJECT_ID(N'opportunities.MajorProjectsInventory'))
BEGIN
    CREATE INDEX IX_MPI_GeneralContractorCanonicalOrgId
        ON opportunities.MajorProjectsInventory (GeneralContractorCanonicalOrgId)
        WHERE GeneralContractorCanonicalOrgId IS NOT NULL;
END;
GO
