/* Kor.OpportunitiesDb migration 259: add FK constraints on
   MajorProjectsInventory.StructuralEngineerCanonicalOrgId and
   .GeneralContractorCanonicalOrgId -> opportunities.CanonicalOrg(Id).

   Proponent + Architect already have FKs (FK_MPI_ProponentCanonicalOrg /
   FK_MPI_ArchitectCanonicalOrg, ON DELETE NO ACTION); SE + GC never did, which
   let merges/deletes strand the FK (16 SE/GC orphans had to be repaired
   2026-06-21). This mirrors the existing two exactly.

   ON DELETE NO ACTION is intentional: BdCanonicalDedup lists SE/GC in its
   FkTargets and repoints them onto the survivor BEFORE deleting a loser org,
   so a delete never hits these constraints. WITH CHECK (the default for ADD
   CONSTRAINT) validates current data — there are 0 orphans, so the FKs become
   trusted. Idempotent: guarded by sys.foreign_keys existence checks. */

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MPI_StructuralEngineerCanonicalOrg')
BEGIN
    ALTER TABLE opportunities.MajorProjectsInventory
        ADD CONSTRAINT FK_MPI_StructuralEngineerCanonicalOrg
            FOREIGN KEY (StructuralEngineerCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MPI_GeneralContractorCanonicalOrg')
BEGIN
    ALTER TABLE opportunities.MajorProjectsInventory
        ADD CONSTRAINT FK_MPI_GeneralContractorCanonicalOrg
            FOREIGN KEY (GeneralContractorCanonicalOrgId) REFERENCES opportunities.CanonicalOrg(Id);
END;
GO
