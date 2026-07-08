/*
    Kor.OpportunitiesDb migration 275.

    Wire up the day-one-but-unused opportunities.OpportunityFiles table for
    pursuit attachments (Ian, 2026-07-08: "drag OR browse a file/email/video
    onto the pursuit"). Storage is a LAN share (LocalPath), not SharePoint.

    The table originally keyed ONLY on OpportunityId (NOT NULL), so BD-tracking
    pursuits (which have no parent Opportunity) could never attach. Every
    pursuit IS a CrmEngagement, so we key attachments on EngagementId and make
    OpportunityId optional.

    Additive + idempotent. Both parent FKs are ON DELETE NO ACTION: pursuits
    and opportunities are soft-retired (RetiredAtUtc), never hard-deleted, so
    cascade isn't needed — and NO ACTION avoids the multiple-cascade-path error
    that two cascading FKs into the same graph would raise.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. OpportunityId becomes optional (a BD-tracking pursuit has none).
IF EXISTS (SELECT 1 FROM sys.foreign_keys
           WHERE name = N'FK_Opp_Files_Opportunity'
             AND parent_object_id = OBJECT_ID(N'opportunities.OpportunityFiles'))
BEGIN
    ALTER TABLE opportunities.OpportunityFiles DROP CONSTRAINT FK_Opp_Files_Opportunity;
END;
GO

IF COL_LENGTH(N'opportunities.OpportunityFiles', N'OpportunityId') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'opportunities.OpportunityFiles')
                 AND name = N'OpportunityId' AND is_nullable = 0)
BEGIN
    ALTER TABLE opportunities.OpportunityFiles ALTER COLUMN OpportunityId BIGINT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE name = N'FK_Opp_Files_Opportunity'
                 AND parent_object_id = OBJECT_ID(N'opportunities.OpportunityFiles'))
BEGIN
    ALTER TABLE opportunities.OpportunityFiles
        ADD CONSTRAINT FK_Opp_Files_Opportunity FOREIGN KEY (OpportunityId)
        REFERENCES opportunities.Opportunities (Id) ON DELETE NO ACTION;
END;
GO

-- 2. EngagementId — the pursuit the file belongs to (the real key going forward).
IF COL_LENGTH(N'opportunities.OpportunityFiles', N'EngagementId') IS NULL
BEGIN
    ALTER TABLE opportunities.OpportunityFiles ADD EngagementId BIGINT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE name = N'FK_Opp_Files_Engagement'
                 AND parent_object_id = OBJECT_ID(N'opportunities.OpportunityFiles'))
BEGIN
    ALTER TABLE opportunities.OpportunityFiles
        ADD CONSTRAINT FK_Opp_Files_Engagement FOREIGN KEY (EngagementId)
        REFERENCES opportunities.CrmEngagements (Id) ON DELETE NO ACTION;
END;
GO

-- 3. ContentType for the file-type label / icon in the UI.
IF COL_LENGTH(N'opportunities.OpportunityFiles', N'ContentType') IS NULL
BEGIN
    ALTER TABLE opportunities.OpportunityFiles ADD ContentType NVARCHAR(200) NULL;
END;
GO

-- 4. A file must hang off at least one parent.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_Opp_Files_OneParent'
                 AND parent_object_id = OBJECT_ID(N'opportunities.OpportunityFiles'))
BEGIN
    ALTER TABLE opportunities.OpportunityFiles
        ADD CONSTRAINT CK_Opp_Files_OneParent
        CHECK (OpportunityId IS NOT NULL OR EngagementId IS NOT NULL);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'opportunities.OpportunityFiles')
                 AND name = N'IX_Opp_Files_EngagementId')
BEGIN
    CREATE INDEX IX_Opp_Files_EngagementId
        ON opportunities.OpportunityFiles (EngagementId)
        WHERE EngagementId IS NOT NULL;
END;
GO

PRINT 'Migration 275 complete.';
GO
