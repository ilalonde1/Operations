/*
    Kor.OpportunitiesDb migration 270 — widen *StaffId columns to hold a UPN.

    The app's identity (ResolveActor) is the signed-in UPN, e.g.
    'ilalonde@korstructural.com' (~26 chars). The owner/audit columns
    CreatedBy / UpdatedBy are already nvarchar(150), but the *StaffId
    columns were sized nvarchar(20) for a short staff code the app never
    actually produces. With 0 opportunities ever owned, nothing hit this —
    until the Bazaar Grab writes OwnerStaffId = UPN and SQL Server raises
    "String or binary data would be truncated". (Caught by a reversible
    live grab test before the feature shipped.)

    Widen to nvarchar(150) to match the CreatedBy/UpdatedBy convention:
      - opportunities.Opportunities.OwnerStaffId            (no index deps)
      - opportunities.OpportunityAssignmentLog.To/By/FromStaffId (no index deps)
      - opportunities.CrmEngagements.OwnerStaffId           (3 index deps — drop+recreate)

    Widening nvarchar is metadata-only (no data rewrite, no truncation).
    Idempotent: each ALTER is gated on the column still being length 20, and
    the dropped indexes are recreated only if missing. Reversible by the
    inverse ALTERs, though there is no reason to narrow back.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Opportunities.OwnerStaffId (nullable, no index dependency) -----------------
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_SCHEMA='opportunities' AND TABLE_NAME='Opportunities'
             AND COLUMN_NAME='OwnerStaffId' AND CHARACTER_MAXIMUM_LENGTH=20)
    ALTER TABLE opportunities.Opportunities ALTER COLUMN OwnerStaffId nvarchar(150) NULL;
GO

-- 2. OpportunityAssignmentLog staff columns (nullable, no index dependency) -----
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_SCHEMA='opportunities' AND TABLE_NAME='OpportunityAssignmentLog'
             AND COLUMN_NAME='ToStaffId' AND CHARACTER_MAXIMUM_LENGTH=20)
    ALTER TABLE opportunities.OpportunityAssignmentLog ALTER COLUMN ToStaffId nvarchar(150) NULL;
GO
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_SCHEMA='opportunities' AND TABLE_NAME='OpportunityAssignmentLog'
             AND COLUMN_NAME='ByStaffId' AND CHARACTER_MAXIMUM_LENGTH=20)
    ALTER TABLE opportunities.OpportunityAssignmentLog ALTER COLUMN ByStaffId nvarchar(150) NULL;
GO
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_SCHEMA='opportunities' AND TABLE_NAME='OpportunityAssignmentLog'
             AND COLUMN_NAME='FromStaffId' AND CHARACTER_MAXIMUM_LENGTH=20)
    ALTER TABLE opportunities.OpportunityAssignmentLog ALTER COLUMN FromStaffId nvarchar(150) NULL;
GO

-- 3. CrmEngagements.OwnerStaffId — drop the 3 dependent indexes, widen, recreate.
--    OwnerStaffId is an INCLUDE column of IX_CrmEngagements_Region and
--    IX_CrmEngagements_Stage, and a KEY column of the unique filtered index
--    UX_CrmEngagements_BdRelationship. SQL Server blocks ALTER COLUMN while any
--    index references the column, so all three are dropped here and recreated
--    verbatim in the next batch.
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_SCHEMA='opportunities' AND TABLE_NAME='CrmEngagements'
             AND COLUMN_NAME='OwnerStaffId' AND CHARACTER_MAXIMUM_LENGTH=20)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CrmEngagements_Region' AND object_id=OBJECT_ID('opportunities.CrmEngagements'))
        DROP INDEX IX_CrmEngagements_Region ON opportunities.CrmEngagements;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CrmEngagements_Stage' AND object_id=OBJECT_ID('opportunities.CrmEngagements'))
        DROP INDEX IX_CrmEngagements_Stage ON opportunities.CrmEngagements;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_CrmEngagements_BdRelationship' AND object_id=OBJECT_ID('opportunities.CrmEngagements'))
        DROP INDEX UX_CrmEngagements_BdRelationship ON opportunities.CrmEngagements;

    ALTER TABLE opportunities.CrmEngagements ALTER COLUMN OwnerStaffId nvarchar(150) NULL;
END
GO

-- Recreate the three indexes exactly as they were (guarded so a re-run is a no-op).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CrmEngagements_Region' AND object_id=OBJECT_ID('opportunities.CrmEngagements'))
    CREATE NONCLUSTERED INDEX IX_CrmEngagements_Region
        ON opportunities.CrmEngagements (Region)
        INCLUDE (OwnerStaffId, BuyerCanonicalOrgId, Stage, ProposalsSubmittedCad, ProposalsAcceptedCad)
        WHERE Region IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CrmEngagements_Stage' AND object_id=OBJECT_ID('opportunities.CrmEngagements'))
    CREATE NONCLUSTERED INDEX IX_CrmEngagements_Stage
        ON opportunities.CrmEngagements (Stage)
        INCLUDE (OpportunityId, OwnerStaffId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_CrmEngagements_BdRelationship' AND object_id=OBJECT_ID('opportunities.CrmEngagements'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_CrmEngagements_BdRelationship
        ON opportunities.CrmEngagements (BuyerCanonicalOrgId, OwnerStaffId, Region)
        WHERE (OpportunityId IS NULL AND BuyerCanonicalOrgId IS NOT NULL AND OwnerStaffId IS NOT NULL AND Region IS NOT NULL);
GO

PRINT 'Migration 270 complete.';
GO
